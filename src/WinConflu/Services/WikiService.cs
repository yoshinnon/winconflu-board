// ============================================================
// WinConflu.NET — WikiService
// HierarchyId 階層操作 + フルテキスト検索
// ============================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using WinConflu.Data;
using WinConflu.Models;

namespace WinConflu.Services;

public interface IWikiService
{
    Task<Page?>           GetPageAsync(int id, CancellationToken ct = default);
    Task<Page?>           GetPageBySlugAsync(string slug, CancellationToken ct = default);
    Task<List<Page>>      GetChildrenAsync(int? parentId, CancellationToken ct = default);
    Task<List<Page>>      GetBreadcrumbAsync(int pageId, CancellationToken ct = default);
    Task<List<Page>>      GetSubtreeAsync(int pageId, CancellationToken ct = default);
    Task<Page>            CreatePageAsync(CreatePageRequest req, string authorSid, CancellationToken ct = default);
    Task<Page>            UpdatePageAsync(int id, UpdatePageRequest req, string authorSid, CancellationToken ct = default);
    Task                  DeletePageAsync(int id, string authorSid, CancellationToken ct = default);
    Task<List<SearchResult>> SearchAsync(string query, int maxResults = 50, CancellationToken ct = default);
    Task<List<Revision>>  GetRevisionsAsync(int pageId, CancellationToken ct = default);
    Task<Page>            RollbackAsync(int pageId, int revisionId, string authorSid, CancellationToken ct = default);
}

public record CreatePageRequest(
    string Title,
    string Content,
    string ContentFormat,  // "json" | "markdown"
    int?   ParentId,
    string? Slug,
    string? AdGroupSid);

public record UpdatePageRequest(
    string Title,
    string Content,
    string ContentFormat,  // "json" | "markdown"
    string? Slug,
    string? AdGroupSid,
    string? ChangeComment);

public record SearchResult(
    int    Id,
    string Title,
    string Snippet,
    string EntityType,   // "Page" or "Issue"
    double Rank,
    DateTimeOffset UpdatedAt = default);

public class WikiService(
    AppDbContext db,
    IMemoryCache cache,
    IAuditService audit,
    IProseMirrorService proseMirror) : IWikiService
{
    private static readonly MemoryCacheEntryOptions _shortCache =
        new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromMinutes(5));

    // ── ページ取得 ─────────────────────────────────────────

    public async Task<Page?> GetPageAsync(int id, CancellationToken ct = default)
        => await db.Pages
            .Include(p => p.LinkedIssues)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);

    public async Task<Page?> GetPageBySlugAsync(string slug, CancellationToken ct = default)
        => await db.Pages
            .FirstOrDefaultAsync(p => p.Slug == slug && !p.IsDeleted, ct);

    // ── 子ページ一覧（HierarchyId の直接の子） ────────────
    public async Task<List<Page>> GetChildrenAsync(int? parentId, CancellationToken ct = default)
    {
        var cacheKey = $"page_children_{parentId ?? 0}";
        if (cache.TryGetValue(cacheKey, out List<Page>? cached))
            return cached!;

        HierarchyId parentPath;
        if (parentId.HasValue)
        {
            var parent = await db.Pages.FindAsync([parentId.Value], ct);
            parentPath = parent?.Path ?? HierarchyId.GetRoot();
        }
        else
        {
            parentPath = HierarchyId.GetRoot();
        }

        // HierarchyId.GetLevel() = 1 で直接の子のみ取得
        var children = await db.Pages
            .Where(p => !p.IsDeleted
                     && p.Path.GetAncestor(1) == parentPath)
            .OrderBy(p => p.Path)
            .ToListAsync(ct);

        cache.Set(cacheKey, children, _shortCache);
        return children;
    }

    // ── パンくずリスト（ルートから対象ページまで） ──────
    public async Task<List<Page>> GetBreadcrumbAsync(int pageId, CancellationToken ct = default)
    {
        var page = await db.Pages.FindAsync([pageId], ct);
        if (page is null) return [];

        // HierarchyId の GetAncestor() を使って全祖先を一括取得
        var depth = page.Path.GetLevel();
        var ancestorPaths = Enumerable.Range(0, depth)
            .Select(i => page.Path.GetAncestor(depth - i))
            .ToList();

        return await db.Pages
            .Where(p => ancestorPaths.Contains(p.Path) && !p.IsDeleted)
            .OrderBy(p => p.Path.GetLevel())
            .ToListAsync(ct);
    }

    // ── サブツリー全取得（移動・削除操作用） ────────────
    public async Task<List<Page>> GetSubtreeAsync(int pageId, CancellationToken ct = default)
    {
        var page = await db.Pages.FindAsync([pageId], ct);
        if (page is null) return [];

        return await db.Pages
            .Where(p => !p.IsDeleted && p.Path.IsDescendantOf(page.Path))
            .OrderBy(p => p.Path)
            .ToListAsync(ct);
    }

    // ── ページ作成 ─────────────────────────────────────────
    public async Task<Page> CreatePageAsync(
        CreatePageRequest req,
        string authorSid,
        CancellationToken ct = default)
    {
        HierarchyId parentPath;
        if (req.ParentId.HasValue)
        {
            var parent = await db.Pages.FindAsync([req.ParentId.Value], ct)
                ?? throw new InvalidOperationException($"親ページ {req.ParentId} が見つかりません。");
            parentPath = parent.Path;
        }
        else
        {
            parentPath = HierarchyId.GetRoot();
        }

        var lastSibling = await db.Pages
            .Where(p => p.Path.GetAncestor(1) == parentPath && !p.IsDeleted)
            .OrderByDescending(p => p.Path)
            .Select(p => p.Path)
            .FirstOrDefaultAsync(ct);

        var newPath = parentPath.GetDescendant(lastSibling, null);

        // プレーンテキストキャッシュを生成
        var plainText = req.ContentFormat == "json"
            ? proseMirror.ToPlainText(req.Content)
            : req.Content;  // Markdown はそのまま FTS に使う

        var page = new Page
        {
            Title         = req.Title,
            Content       = req.Content,
            ContentFormat = req.ContentFormat,
            ContentText   = plainText,
            Path          = newPath,
            AdGroupSid    = req.AdGroupSid,
            Slug          = req.Slug,
            CreatedBy     = authorSid,
            UpdatedBy     = authorSid
        };

        db.Pages.Add(page);

        db.Revisions.Add(new Revision
        {
            Page          = page,
            Content       = req.Content,
            ContentFormat = req.ContentFormat,
            AuthorSid     = authorSid,
            Version       = 1,
            Comment       = "初回作成"
        });

        await db.SaveChangesAsync(ct);
        InvalidateChildCache(req.ParentId);

        await audit.LogAsync("Create", "Page", page.Id, authorSid, null, req.Title);
        return page;
    }

    // ── ページ更新 ─────────────────────────────────────────
    public async Task<Page> UpdatePageAsync(
        int id,
        UpdatePageRequest req,
        string authorSid,
        CancellationToken ct = default)
    {
        var page = await db.Pages
            .Include(p => p.Revisions)
            .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct)
            ?? throw new InvalidOperationException($"ページ {id} が見つかりません。");

        var oldTitle  = page.Title;
        var plainText = req.ContentFormat == "json"
            ? proseMirror.ToPlainText(req.Content)
            : req.Content;

        page.Title         = req.Title;
        page.Content       = req.Content;
        page.ContentFormat = req.ContentFormat;
        page.ContentText   = plainText;
        page.Slug          = req.Slug;
        page.AdGroupSid    = req.AdGroupSid;
        page.UpdatedBy     = authorSid;
        page.UpdatedAt     = DateTimeOffset.UtcNow;

        db.Revisions.Add(new Revision
        {
            PageId        = page.Id,
            Content       = req.Content,
            ContentFormat = req.ContentFormat,
            AuthorSid     = authorSid,
            Version       = page.Revisions.Count + 1,
            Comment       = req.ChangeComment
        });

        await db.SaveChangesAsync(ct);
        cache.Remove($"page_{id}");

        await audit.LogAsync("Update", "Page", page.Id, authorSid, oldTitle, req.Title);
        return page;
    }

    // ── ページ削除（論理削除） ─────────────────────────────
    public async Task DeletePageAsync(int id, string authorSid, CancellationToken ct = default)
    {
        var page = await db.Pages.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"ページ {id} が見つかりません。");

        page.IsDeleted = true;
        page.UpdatedAt = DateTimeOffset.UtcNow;
        page.UpdatedBy = authorSid;

        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Delete", "Page", id, authorSid, page.Title, null);
    }

    // ── フルテキスト検索（SQL Server FTS） ────────────────
    public async Task<List<SearchResult>> SearchAsync(
        string query,
        int maxResults = 50,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        // SQL Server CONTAINSTABLE を使用した全文検索
        // EF Core では FromSqlRaw で呼び出し
        var sql = """
            SELECT TOP (@maxResults)
                p.Id,
                p.Title,
                LEFT(p.Content, 200) AS Snippet,
                'Page'              AS EntityType,
                kt.[RANK]           AS Rank
            FROM Pages p
            INNER JOIN CONTAINSTABLE(Pages, (Title, Content), @query) AS kt
                ON p.Id = kt.[KEY]
            WHERE p.IsDeleted = 0
            UNION ALL
            SELECT TOP (@maxResults)
                i.Id,
                i.Title,
                LEFT(i.Description, 200) AS Snippet,
                'Issue'             AS EntityType,
                ki.[RANK]           AS Rank
            FROM Issues i
            INNER JOIN CONTAINSTABLE(Issues, (Title, Description), @query) AS ki
                ON i.Id = ki.[KEY]
            WHERE i.IsDeleted = 0
            ORDER BY Rank DESC
            """;

        return await db.Database
            .SqlQueryRaw<SearchResult>(sql,
                new Microsoft.Data.SqlClient.SqlParameter("@query", $"\"{query}*\""),
                new Microsoft.Data.SqlClient.SqlParameter("@maxResults", maxResults))
            .ToListAsync(ct);
    }

    // ── 履歴・ロールバック ────────────────────────────────
    public async Task<List<Revision>> GetRevisionsAsync(int pageId, CancellationToken ct = default)
        => await db.Revisions
            .Where(r => r.PageId == pageId)
            .OrderByDescending(r => r.Version)
            .ToListAsync(ct);

    public async Task<Page> RollbackAsync(
        int pageId, int revisionId,
        string authorSid,
        CancellationToken ct = default)
    {
        var revision = await db.Revisions
            .FirstOrDefaultAsync(r => r.Id == revisionId && r.PageId == pageId, ct)
            ?? throw new InvalidOperationException("指定されたリビジョンが見つかりません。");

        var page      = await db.Pages.FindAsync([pageId], ct)!;
        var updateReq = new UpdatePageRequest(
            Title:         page!.Title,
            Content:       revision.Content,
            ContentFormat: revision.ContentFormat,
            Slug:          null,
            AdGroupSid:    null,
            ChangeComment: $"v{revision.Version} へロールバック");

        return await UpdatePageAsync(pageId, updateReq, authorSid, ct);
    }

    private void InvalidateChildCache(int? parentId)
        => cache.Remove($"page_children_{parentId ?? 0}");
}
