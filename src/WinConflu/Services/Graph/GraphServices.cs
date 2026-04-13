// ============================================================
// WinConflu.NET — Microsoft Graph サービス (Phase 4 完全実装)
// Managed Identity で認証、MemoryCache でスロットル制御
// ============================================================

using Azure.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace WinConflu.Services.Graph;

// ────────────────────────────────────────────────────────────
// GraphServiceClientFactory
// ────────────────────────────────────────────────────────────

public class GraphServiceClientFactory(IConfiguration config, ILogger<GraphServiceClientFactory> logger)
{
    private GraphServiceClient? _client;

    public GraphServiceClient GetClient()
    {
        if (_client is not null) return _client;

        // Azure 環境では Managed Identity、ローカルでは VisualStudioCredential 等にフォールバック
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ExcludeSharedTokenCacheCredential = true
        });

        var scopes = config.GetSection("Graph:Scopes").Get<string[]>()
                     ?? ["https://graph.microsoft.com/.default"];

        logger.LogInformation("Graph クライアント初期化: scopes={Scopes}", string.Join(",", scopes));
        _client = new GraphServiceClient(credential, scopes);
        return _client;
    }
}

// ────────────────────────────────────────────────────────────
// IPresenceService / TeamsPresenceService
// ────────────────────────────────────────────────────────────

public interface IPresenceService
{
    Task<UserPresence>       GetPresenceAsync(string userId, CancellationToken ct = default);
    Task<List<UserPresence>> GetBulkPresenceAsync(IEnumerable<string> userIds, CancellationToken ct = default);
}

public record UserPresence(
    string UserId,
    string Availability,
    string Activity,
    string StatusMessage);

public class TeamsPresenceService(
    GraphServiceClientFactory factory,
    IMemoryCache cache,
    ILogger<TeamsPresenceService> logger) : IPresenceService
{
    // プレゼンスは 30 秒でキャッシュ失効（Graph API スロットル対策）
    private static readonly TimeSpan _ttl = TimeSpan.FromSeconds(30);

    public async Task<UserPresence> GetPresenceAsync(
        string userId, CancellationToken ct = default)
    {
        var key = $"presence:{userId}";
        if (cache.TryGetValue(key, out UserPresence? cached)) return cached!;

        try
        {
            var graph    = factory.GetClient();
            var presence = await graph.Users[userId].Presence.GetAsync(cancellationToken: ct);

            var result = new UserPresence(
                UserId:        userId,
                Availability:  presence?.Availability ?? "Unknown",
                Activity:      presence?.Activity     ?? "Unknown",
                StatusMessage: presence?.StatusMessage?.Message ?? string.Empty);

            cache.Set(key, result, new MemoryCacheEntryOptions().SetAbsoluteExpiration(_ttl));
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "プレゼンス取得失敗: {UserId}", userId);
            return new UserPresence(userId, "Unknown", "Unknown", string.Empty);
        }
    }

    public async Task<List<UserPresence>> GetBulkPresenceAsync(
        IEnumerable<string> userIds, CancellationToken ct = default)
    {
        // 最大 650 ユーザーまでバッチ API で一括取得（Graph の getPresencesByUserId）
        var ids = userIds.ToList();
        if (ids.Count == 0) return [];

        // キャッシュヒット分は Graph を叩かない
        var result     = new List<UserPresence>();
        var uncachedIds = new List<string>();

        foreach (var id in ids)
        {
            if (cache.TryGetValue($"presence:{id}", out UserPresence? cached))
                result.Add(cached!);
            else
                uncachedIds.Add(id);
        }

        if (uncachedIds.Any())
        {
            try
            {
                var graph    = factory.GetClient();
                var response = await graph.Communications.GetPresencesByUserId
                    .PostAsGetPresencesByUserIdPostResponseAsync(
                        new Microsoft.Graph.Communications.GetPresencesByUserId.GetPresencesByUserIdPostRequestBody
                        {
                            Ids = uncachedIds
                        }, cancellationToken: ct);

                foreach (var p in response?.Value ?? [])
                {
                    var up = new UserPresence(
                        p.Id ?? string.Empty,
                        p.Availability ?? "Unknown",
                        p.Activity ?? "Unknown",
                        p.StatusMessage?.Message ?? string.Empty);

                    result.Add(up);
                    cache.Set($"presence:{up.UserId}", up,
                        new MemoryCacheEntryOptions().SetAbsoluteExpiration(_ttl));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "バルクプレゼンス取得失敗");
                // 失敗分は Unknown で補完
                result.AddRange(uncachedIds.Select(id =>
                    new UserPresence(id, "Unknown", "Unknown", string.Empty)));
            }
        }

        return result;
    }
}

// ────────────────────────────────────────────────────────────
// IUserProfileService — AD ユーザー情報（表示名・部署・顔写真）
// ────────────────────────────────────────────────────────────

public interface IUserProfileService
{
    Task<GraphUserProfile?>       GetProfileAsync(string userId, CancellationToken ct = default);
    Task<List<GraphUserProfile>>  SearchUsersAsync(string query, int top = 10, CancellationToken ct = default);
    Task<byte[]?>                 GetPhotoAsync(string userId, CancellationToken ct = default);
}

public record GraphUserProfile(
    string Id,
    string DisplayName,
    string? Department,
    string? JobTitle,
    string? Mail,
    string? UserPrincipalName,
    string? PhotoBase64
);

public class UserProfileService(
    GraphServiceClientFactory factory,
    IMemoryCache cache,
    ILogger<UserProfileService> logger) : IUserProfileService
{
    private static readonly TimeSpan _profileTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan _photoTtl   = TimeSpan.FromHours(6);

    public async Task<GraphUserProfile?> GetProfileAsync(
        string userId, CancellationToken ct = default)
    {
        var key = $"profile:{userId}";
        if (cache.TryGetValue(key, out GraphUserProfile? cached)) return cached;

        try
        {
            var graph = factory.GetClient();
            var user  = await graph.Users[userId].GetAsync(req =>
            {
                req.QueryParameters.Select =
                [
                    "id", "displayName", "department",
                    "jobTitle", "mail", "userPrincipalName"
                ];
            }, ct);

            if (user == null) return null;

            var profile = new GraphUserProfile(
                user.Id            ?? userId,
                user.DisplayName   ?? userId,
                user.Department,
                user.JobTitle,
                user.Mail,
                user.UserPrincipalName,
                PhotoBase64: null   // 顔写真は別途 GetPhotoAsync で取得
            );

            cache.Set(key, profile, new MemoryCacheEntryOptions().SetAbsoluteExpiration(_profileTtl));
            return profile;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ユーザープロファイル取得失敗: {UserId}", userId);
            return null;
        }
    }

    public async Task<List<GraphUserProfile>> SearchUsersAsync(
        string query, int top = 10, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        try
        {
            var graph = factory.GetClient();
            var users = await graph.Users.GetAsync(req =>
            {
                req.QueryParameters.Search  = $"\"displayName:{query}\" OR \"mail:{query}\"";
                req.QueryParameters.Select  = ["id", "displayName", "department", "mail", "userPrincipalName"];
                req.QueryParameters.Top     = top;
                req.QueryParameters.Orderby = ["displayName"];
                req.Headers.Add("ConsistencyLevel", "eventual");
            }, ct);

            return (users?.Value ?? [])
                .Select(u => new GraphUserProfile(
                    u.Id ?? string.Empty, u.DisplayName ?? string.Empty,
                    u.Department, u.JobTitle, u.Mail, u.UserPrincipalName, null))
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ユーザー検索失敗: {Query}", query);
            return [];
        }
    }

    public async Task<byte[]?> GetPhotoAsync(string userId, CancellationToken ct = default)
    {
        var key = $"photo:{userId}";
        if (cache.TryGetValue(key, out byte[]? cached)) return cached;

        try
        {
            var graph  = factory.GetClient();
            var stream = await graph.Users[userId].Photo.Content.GetAsync(cancellationToken: ct);
            if (stream == null) return null;

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            cache.Set(key, bytes, new MemoryCacheEntryOptions().SetAbsoluteExpiration(_photoTtl));
            return bytes;
        }
        catch
        {
            // 顔写真なしは正常（設定していないユーザーが多い）
            return null;
        }
    }
}

// ────────────────────────────────────────────────────────────
// ICalendarService — Outlook 予定表連携
// ────────────────────────────────────────────────────────────

public interface ICalendarService
{
    Task UpsertTaskAsync(
        string userSid, string subject, DateTimeOffset dueDate,
        string? description, string externalId, CancellationToken ct = default);

    Task DeleteTaskAsync(string userSid, string externalId, CancellationToken ct = default);
}

public class OutlookCalendarService(
    GraphServiceClientFactory factory,
    ILogger<OutlookCalendarService> logger) : ICalendarService
{
    // WinConflu が作成したイベントを識別するための拡張属性スキーマ ID
    private const string ExtensionName = "com.winconflu.issueId";

    public async Task UpsertTaskAsync(
        string userSid, string subject, DateTimeOffset dueDate,
        string? description, string externalId, CancellationToken ct = default)
    {
        try
        {
            var graph = factory.GetClient();

            // 外部 ID で既存イベントを検索
            var existing = await graph.Users[userSid].Events.GetAsync(cfg =>
            {
                cfg.QueryParameters.Filter = $"singleValueExtendedProperties/any(ep: ep/id eq 'String {{00020329-0000-0000-C000-000000000046}} Name {ExtensionName}' and ep/value eq '{externalId}')";
                cfg.QueryParameters.Top    = 1;
                cfg.QueryParameters.Select = ["id", "subject"];
            }, ct);

            var eventBody = new Event
            {
                Subject = $"[WinConflu] {subject}",
                Body    = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content     = $"<p>{System.Net.WebUtility.HtmlEncode(description ?? string.Empty)}</p>" +
                                  $"<hr/><p style='color:gray;font-size:11px'>WinConflu Issue ID: {externalId}</p>"
                },
                Start = new DateTimeTimeZone
                {
                    DateTime = dueDate.ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "Tokyo Standard Time"
                },
                End = new DateTimeTimeZone
                {
                    DateTime = dueDate.AddHours(1).ToString("yyyy-MM-ddTHH:mm:ss"),
                    TimeZone = "Tokyo Standard Time"
                },
                IsReminderOn = true,
                ReminderMinutesBeforeStart = 60,
                Categories = ["WinConflu"],
                SingleValueExtendedProperties =
                [
                    new SingleValueLegacyExtendedProperty
                    {
                        Id    = $"String {{00020329-0000-0000-C000-000000000046}} Name {ExtensionName}",
                        Value = externalId
                    }
                ]
            };

            if (existing?.Value?.FirstOrDefault() is { } evt)
            {
                await graph.Users[userSid].Events[evt.Id].PatchAsync(eventBody, ct);
                logger.LogInformation("Outlook イベント更新: {Subject}", subject);
            }
            else
            {
                await graph.Users[userSid].Events.PostAsync(eventBody, ct);
                logger.LogInformation("Outlook イベント作成: {Subject}", subject);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Outlook 予定表同期失敗: {ExternalId}", externalId);
            throw;
        }
    }

    public async Task DeleteTaskAsync(
        string userSid, string externalId, CancellationToken ct = default)
    {
        try
        {
            var graph    = factory.GetClient();
            var existing = await graph.Users[userSid].Events.GetAsync(cfg =>
            {
                cfg.QueryParameters.Filter = $"singleValueExtendedProperties/any(ep: ep/id eq 'String {{00020329-0000-0000-C000-000000000046}} Name {ExtensionName}' and ep/value eq '{externalId}')";
                cfg.QueryParameters.Top    = 1;
                cfg.QueryParameters.Select = ["id"];
            }, ct);

            if (existing?.Value?.FirstOrDefault() is { } evt)
            {
                await graph.Users[userSid].Events[evt.Id].DeleteAsync(cancellationToken: ct);
                logger.LogInformation("Outlook イベント削除: {ExternalId}", externalId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Outlook イベント削除失敗: {ExternalId}", externalId);
        }
    }
}
