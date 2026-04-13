// ============================================================
// WinConflu.NET — 補助サービス群
// AuditService / AdGroupService / BlobAttachmentService
// ============================================================

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Caching.Memory;
using WinConflu.Data;
using WinConflu.Models;

namespace WinConflu.Services;

// ────────────────────────────────────────────────────────────
// AuditService — 操作ログ記録
// ────────────────────────────────────────────────────────────

public interface IAuditService
{
    Task LogAsync(string action, string entityType, int? entityId,
                  string userSid, string? oldValue, string? newValue,
                  CancellationToken ct = default);
}

public class AuditService(AppDbContext db, IHttpContextAccessor http) : IAuditService
{
    public async Task LogAsync(
        string action, string entityType, int? entityId,
        string userSid, string? oldValue, string? newValue,
        CancellationToken ct = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Action     = action,
            EntityType = entityType,
            EntityId   = entityId,
            UserSid    = userSid,
            OldValue   = oldValue,
            NewValue   = newValue,
            IpAddress  = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            CreatedAt  = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
    }
}

// ────────────────────────────────────────────────────────────
// AdGroupService — ADグループ情報をキャッシュ付きで取得
// ────────────────────────────────────────────────────────────

public interface IAdGroupService
{
    Task<bool>         IsInGroupAsync(string userSid, string groupSid, CancellationToken ct = default);
    Task<List<string>> GetUserGroupsAsync(string userSid, CancellationToken ct = default);
    Task<AdUserProfile> GetUserProfileAsync(string userSid, CancellationToken ct = default);
}

public record AdUserProfile(
    string Sid,
    string DisplayName,
    string Department,
    string Email,
    string? PhotoUrl);

public class AdGroupService(
    IMemoryCache cache,
    IConfiguration config,
    IHttpContextAccessor _http,
    ILogger<AdGroupService> logger) : IAdGroupService
{
    // ADグループ情報のキャッシュ TTL（appsettings で設定）
    private TimeSpan CacheTtl => TimeSpan.FromSeconds(
        config.GetValue("AdGroup:CacheDurationSeconds", 300));

    public async Task<bool> IsInGroupAsync(
        string userSid, string groupSid, CancellationToken ct = default)
    {
        var groups = await GetUserGroupsAsync(userSid, ct);
        return groups.Contains(groupSid);
    }

    public async Task<List<string>> GetUserGroupsAsync(
        string userSid, CancellationToken ct = default)
    {
        var cacheKey = $"ad_groups_{userSid}";
        if (cache.TryGetValue(cacheKey, out List<string>? cached))
            return cached!;

        var groups = new List<string>();

        // Windows 認証環境: ClaimsPrincipal の GroupSid クレームから取得
        var httpContext = _http.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated == true)
        {
            var groupClaims = httpContext.User.Claims
                .Where(c => c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/groupsid"
                         || c.Type == System.Security.Claims.ClaimTypes.GroupSid
                         || c.Type == "groups")
                .Select(c => c.Value)
                .ToList();
            groups.AddRange(groupClaims);
        }

        cache.Set(cacheKey, groups,
            new MemoryCacheEntryOptions().SetAbsoluteExpiration(CacheTtl));

        logger.LogDebug("ADグループ取得: {UserSid} → {Count} グループ", userSid, groups.Count);
        return groups;
    }

    public async Task<AdUserProfile> GetUserProfileAsync(
        string userSid, CancellationToken ct = default)
    {
        var cacheKey = $"ad_profile_{userSid}";
        if (cache.TryGetValue(cacheKey, out AdUserProfile? cached))
            return Task.FromResult(cached!).Result;

        // HttpContext から Windows 認証ユーザー情報を取得
        var httpContext = _http.HttpContext;
        AdUserProfile profile;

        if (httpContext?.User.Identity?.IsAuthenticated == true)
        {
            var name = httpContext.User.Identity.Name ?? userSid;
            var displayName = httpContext.User.Claims
                .FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Name
                                  || c.Type == "name")?.Value ?? name;
            var email = httpContext.User.Claims
                .FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email
                                  || c.Type == "email"
                                  || c.Type == "upn")?.Value;
            var dept = httpContext.User.Claims
                .FirstOrDefault(c => c.Type == "department")?.Value;

            profile = new AdUserProfile(userSid, displayName, dept ?? "", email ?? "", null);
        }
        else
        {
            profile = new AdUserProfile(userSid, userSid, "", "", null);
        }

        cache.Set(cacheKey, profile,
            new MemoryCacheEntryOptions().SetAbsoluteExpiration(CacheTtl));

        return await Task.FromResult(profile);
    }
}

// ────────────────────────────────────────────────────────────
// BlobAttachmentService — Azure Blob Storage への添付ファイル管理
// ────────────────────────────────────────────────────────────

public interface IAttachmentService
{
    Task<Attachment> UploadAsync(
        Stream stream, string fileName, string contentType,
        string relatedType, int relatedId, string uploaderSid,
        CancellationToken ct = default);

    Task<(Stream Stream, string ContentType)> DownloadAsync(int attachmentId, CancellationToken ct = default);
    Task DeleteAsync(int attachmentId, string userSid, CancellationToken ct = default);
    Task<List<Attachment>> GetForEntityAsync(string relatedType, int relatedId, CancellationToken ct = default);
}

public class BlobAttachmentService(
    AppDbContext db,
    BlobServiceClient blobClient,
    IAuditService audit,
    ILogger<BlobAttachmentService> logger) : IAttachmentService
{
    // コンテナ名の解決（Wiki画像 / 添付ファイルで分離）
    private string ResolveContainer(string contentType, string relatedType)
        => relatedType == "Page" && contentType.StartsWith("image/")
            ? "wiki-images"
            : "attachments";

    public async Task<Attachment> UploadAsync(
        Stream stream, string fileName, string contentType,
        string relatedType, int relatedId, string uploaderSid,
        CancellationToken ct = default)
    {
        var containerName = ResolveContainer(contentType, relatedType);
        var container     = blobClient.GetBlobContainerClient(containerName);

        // ファイル名衝突防止: GUID プレフィックス付き
        var blobName  = $"{relatedType.ToLower()}/{relatedId}/{Guid.NewGuid():N}_{fileName}";
        var blobRef   = container.GetBlobClient(blobName);

        await blobRef.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
        }, ct);

        var sizeBytes = stream.Length > 0 ? stream.Length : 0;

        var attachment = new Attachment
        {
            RelatedType  = relatedType,
            RelatedId    = relatedId,
            FileName     = fileName,
            BlobUrl      = blobRef.Uri.ToString(),
            ContentType  = contentType,
            SizeBytes    = sizeBytes,
            UploadedBy   = uploaderSid,
            UploadedAt   = DateTimeOffset.UtcNow
        };

        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Upload", "Attachment", attachment.Id, uploaderSid, null, fileName);

        logger.LogInformation("Uploaded: {BlobName} ({Size} bytes)", blobName, sizeBytes);
        return attachment;
    }

    public async Task<(Stream Stream, string ContentType)> DownloadAsync(
        int attachmentId, CancellationToken ct = default)
    {
        var attachment = await db.Attachments.FindAsync([attachmentId], ct)
            ?? throw new InvalidOperationException($"添付ファイル {attachmentId} が見つかりません。");

        var uri           = new Uri(attachment.BlobUrl);
        var containerName = uri.Segments[1].TrimEnd('/');
        var blobName      = string.Join("", uri.Segments.Skip(2));

        var container = blobClient.GetBlobContainerClient(containerName);
        var blobRef   = container.GetBlobClient(blobName);

        var response = await blobRef.DownloadStreamingAsync(cancellationToken: ct);
        return (response.Value.Content, attachment.ContentType ?? "application/octet-stream");
    }

    public async Task DeleteAsync(int attachmentId, string userSid, CancellationToken ct = default)
    {
        var attachment = await db.Attachments.FindAsync([attachmentId], ct)
            ?? throw new InvalidOperationException($"添付ファイル {attachmentId} が見つかりません。");

        var uri           = new Uri(attachment.BlobUrl);
        var containerName = uri.Segments[1].TrimEnd('/');
        var blobName      = string.Join("", uri.Segments.Skip(2));

        var container = blobClient.GetBlobContainerClient(containerName);
        await container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: ct);

        db.Attachments.Remove(attachment);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Delete", "Attachment", attachmentId, userSid, attachment.FileName, null);
    }

    public async Task<List<Attachment>> GetForEntityAsync(
        string relatedType, int relatedId, CancellationToken ct = default)
        => await db.Attachments
            .Where(a => a.RelatedType == relatedType && a.RelatedId == relatedId)
            .OrderByDescending(a => a.UploadedAt)
            .ToListAsync(ct);
}
