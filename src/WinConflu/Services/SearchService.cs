// ============================================================
// WinConflu.NET — FullTextSearchService
// SQL Server CONTAINSTABLE による Wiki + Boards 横断全文検索
// ============================================================

using Microsoft.EntityFrameworkCore;
using WinConflu.Data;
using WinConflu.Models;

namespace WinConflu.Services;

public interface ISearchService
{
    Task<SearchResponse> SearchAsync(SearchRequest req, CancellationToken ct = default);
}

public record SearchRequest(
    string  Query,
    int     MaxResults    = 30,
    string? EntityFilter  = null,   // "Page" | "Issue" | null (全て)
    int?    ProjectId     = null);

public record SearchResponse(
    List<SearchResult> Results,
    int Total,
    double ElapsedMs);

public class FullTextSearchService(AppDbContext db) : ISearchService
{
    public async Task<SearchResponse> SearchAsync(
        SearchRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Query))
            return new SearchResponse([], 0, 0);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // CONTAINSTABLE クエリ（前方一致 + フレーズ）
        // 検索クエリを SQL Server FTS 書式に変換
        var ftsQuery = BuildFtsQuery(req.Query);
        var results  = new List<SearchResult>();

        // ── Pages 検索 ──────────────────────────────────────
        if (req.EntityFilter is null or "Page")
        {
            var pageSql = $"""
                SELECT TOP ({req.MaxResults})
                    p.Id        AS Id,
                    p.Title     AS Title,
                    LEFT(
                        CASE WHEN p.ContentFormat = 'json' THEN p.ContentText
                             ELSE p.Content END,
                        300
                    ) AS Snippet,
                    'Page'      AS EntityType,
                    CAST(kt.[RANK] AS float) AS Rank,
                    p.UpdatedAt AS UpdatedAt
                FROM dbo.Pages p
                INNER JOIN CONTAINSTABLE(dbo.Pages, (Title, Content, ContentText), {{0}}) AS kt
                    ON p.Id = kt.[KEY]
                WHERE p.IsDeleted = 0
                """;

            var pageResults = await db.Database
                .SqlQueryRaw<SearchResultRaw>(pageSql, ftsQuery)
                .ToListAsync(ct);

            results.AddRange(pageResults.Select(r => new SearchResult(
                r.Id, r.Title, HighlightSnippet(r.Snippet, req.Query),
                r.EntityType, r.Rank, r.UpdatedAt)));
        }

        // ── Issues 検索 ─────────────────────────────────────
        if (req.EntityFilter is null or "Issue")
        {
            var projectFilter = req.ProjectId.HasValue
                ? $"AND i.ProjectId = {req.ProjectId}"
                : "";

            var issueSql = $"""
                SELECT TOP ({req.MaxResults})
                    i.Id        AS Id,
                    i.Title     AS Title,
                    LEFT(ISNULL(i.Description,''), 300) AS Snippet,
                    'Issue'     AS EntityType,
                    CAST(ki.[RANK] AS float) AS Rank,
                    i.UpdatedAt AS UpdatedAt
                FROM dbo.Issues i
                INNER JOIN CONTAINSTABLE(dbo.Issues, (Title, Description), {{0}}) AS ki
                    ON i.Id = ki.[KEY]
                WHERE i.IsDeleted = 0 {projectFilter}
                """;

            var issueResults = await db.Database
                .SqlQueryRaw<SearchResultRaw>(issueSql, ftsQuery)
                .ToListAsync(ct);

            results.AddRange(issueResults.Select(r => new SearchResult(
                r.Id, r.Title, HighlightSnippet(r.Snippet, req.Query),
                r.EntityType, r.Rank, r.UpdatedAt)));
        }

        // ランク降順・最大件数でカット
        var sorted = results
            .OrderByDescending(r => r.Rank)
            .Take(req.MaxResults)
            .ToList();

        sw.Stop();
        return new SearchResponse(sorted, sorted.Count, sw.Elapsed.TotalMilliseconds);
    }

    // ── FTS クエリ変換（前方一致 + フレーズ両対応） ───────────
    private static string BuildFtsQuery(string input)
    {
        var terms = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return "\"\"";

        if (terms.Length == 1)
            return $"\"{EscapeFts(terms[0])}*\"";

        // 複数ワードはフレーズ検索 + 個別前方一致のOR
        var phrase    = $"\"{EscapeFts(input)}\"";
        var individual = string.Join(" OR ", terms.Select(t => $"\"{EscapeFts(t)}*\""));
        return $"({phrase} OR ({individual}))";
    }

    private static string EscapeFts(string term)
        => term.Replace("\"", "\"\"").Replace("'", "''");

    // ── スニペットのキーワードハイライト ─────────────────────
    private static string HighlightSnippet(string snippet, string query)
    {
        if (string.IsNullOrEmpty(snippet)) return string.Empty;

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = System.Net.WebUtility.HtmlEncode(snippet);

        foreach (var term in terms)
        {
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                System.Text.RegularExpressions.Regex.Escape(term),
                m => $"<mark class=\"wcn-search-hit\">{m.Value}</mark>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return result;
    }

    // EF Core SqlQueryRaw 用の内部 DTO
    private record SearchResultRaw(
        int    Id,
        string Title,
        string Snippet,
        string EntityType,
        double Rank,
        DateTimeOffset UpdatedAt);
}
