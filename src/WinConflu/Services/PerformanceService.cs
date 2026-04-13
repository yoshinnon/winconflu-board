// ============================================================
// WinConflu.NET — PerformanceService (Phase 5)
// スロークエリ検出 / DB インデックス状態確認 / 接続プール監視
// ============================================================

using Microsoft.EntityFrameworkCore;
using WinConflu.Data;

namespace WinConflu.Services;

public interface IPerformanceService
{
    Task<PerformanceSummary> GetSummaryAsync(CancellationToken ct = default);
    Task<List<SlowQueryInfo>> GetSlowQueriesAsync(int topN = 20, CancellationToken ct = default);
    Task<List<IndexUsageInfo>> GetIndexUsageAsync(CancellationToken ct = default);
    Task RebuildFragmentedIndexesAsync(int thresholdPct = 30, CancellationToken ct = default);
}

public record PerformanceSummary(
    long   TotalPages,
    long   TotalIssues,
    long   TotalRevisions,
    long   TotalAnnotations,
    long   TotalNotifications,
    double AvgPageResponseMs,
    int    ActiveConnections,
    DateTimeOffset GeneratedAt);

public record SlowQueryInfo(
    string SqlText,
    long   ExecutionCount,
    double AvgDurationMs,
    double TotalDurationMs,
    DateTimeOffset LastExecution);

public record IndexUsageInfo(
    string TableName,
    string IndexName,
    double FragmentationPct,
    long   UserSeeks,
    long   UserScans,
    bool   NeedsRebuild);

public class PerformanceService(
    AppDbContext db,
    ILogger<PerformanceService> logger) : IPerformanceService
{
    // ── サマリー ─────────────────────────────────────────────

    public async Task<PerformanceSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        var pages       = await db.Pages.LongCountAsync(p => !p.IsDeleted, ct);
        var issues      = await db.Issues.LongCountAsync(i => !i.IsDeleted, ct);
        var revisions   = await db.Revisions.LongCountAsync(ct);
        var annotations = await db.InlineAnnotations.LongCountAsync(a => !a.IsDeleted, ct);
        var notifs      = await db.AppNotifications.LongCountAsync(ct);

        return new PerformanceSummary(
            pages, issues, revisions, annotations, notifs,
            AvgPageResponseMs:  0,  // Application Insights から取得（別途実装）
            ActiveConnections:  0,
            GeneratedAt:        DateTimeOffset.UtcNow);
    }

    // ── スロークエリ分析（SQL Server DMV 利用） ──────────────

    public async Task<List<SlowQueryInfo>> GetSlowQueriesAsync(
        int topN = 20, CancellationToken ct = default)
    {
        var sql = $"""
            SELECT TOP ({topN})
                SUBSTRING(st.text, (qs.statement_start_offset/2)+1,
                    ((CASE qs.statement_end_offset
                        WHEN -1 THEN DATALENGTH(st.text)
                        ELSE qs.statement_end_offset END
                    - qs.statement_start_offset)/2)+1) AS SqlText,
                qs.execution_count                              AS ExecutionCount,
                qs.total_elapsed_time / qs.execution_count / 1000.0 AS AvgDurationMs,
                qs.total_elapsed_time / 1000.0                 AS TotalDurationMs,
                qs.last_execution_time                         AS LastExecution
            FROM sys.dm_exec_query_stats qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
            WHERE qs.execution_count > 5
              AND st.text NOT LIKE '%sys.dm%'
              AND st.text NOT LIKE '%Hangfire%'
            ORDER BY qs.total_elapsed_time DESC
            """;

        try
        {
            return await db.Database
                .SqlQueryRaw<SlowQueryInfo>(sql)
                .ToListAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "スロークエリ取得失敗（VIEW SERVER STATE 権限が必要）");
            return [];
        }
    }

    // ── インデックス断片化チェック ────────────────────────────

    public async Task<List<IndexUsageInfo>> GetIndexUsageAsync(CancellationToken ct = default)
    {
        // WinConflu が管理するテーブルのみ対象
        var tables = new[]
        {
            "Pages", "Issues", "Revisions", "InlineAnnotations",
            "AnnotationReplies", "AppNotifications", "AuditLogs"
        };
        var tableList = string.Join(",", tables.Select(t => $"'{t}'"));

        var sql = $"""
            SELECT
                t.name                                         AS TableName,
                i.name                                         AS IndexName,
                CAST(ips.avg_fragmentation_in_percent AS float) AS FragmentationPct,
                ISNULL(us.user_seeks, 0)                       AS UserSeeks,
                ISNULL(us.user_scans, 0)                       AS UserScans,
                CASE WHEN ips.avg_fragmentation_in_percent > 30 THEN 1 ELSE 0 END AS NeedsRebuild
            FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') ips
            INNER JOIN sys.indexes i
                ON ips.object_id = i.object_id AND ips.index_id = i.index_id
            INNER JOIN sys.tables t
                ON i.object_id = t.object_id
            LEFT JOIN sys.dm_db_index_usage_stats us
                ON i.object_id = us.object_id AND i.index_id = us.index_id
                AND us.database_id = DB_ID()
            WHERE t.name IN ({tableList})
              AND i.index_id > 0
              AND ips.avg_fragmentation_in_percent > 5
            ORDER BY ips.avg_fragmentation_in_percent DESC
            """;

        try
        {
            return await db.Database.SqlQueryRaw<IndexUsageInfo>(sql).ToListAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "インデックス情報取得失敗");
            return [];
        }
    }

    // ── 断片化インデックスの自動再構築 ───────────────────────

    public async Task RebuildFragmentedIndexesAsync(
        int thresholdPct = 30, CancellationToken ct = default)
    {
        var indexes = await GetIndexUsageAsync(ct);
        var targets = indexes.Where(i => i.FragmentationPct >= thresholdPct).ToList();

        if (targets.Count == 0)
        {
            logger.LogInformation("インデックス再構築: 対象なし（閾値 {Pct}%）", thresholdPct);
            return;
        }

        foreach (var idx in targets)
        {
            var sql = idx.FragmentationPct >= 30
                ? $"ALTER INDEX [{idx.IndexName}] ON dbo.[{idx.TableName}] REBUILD WITH (ONLINE = ON)"
                : $"ALTER INDEX [{idx.IndexName}] ON dbo.[{idx.TableName}] REORGANIZE";

            try
            {
                await db.Database.ExecuteSqlRawAsync(sql, ct);
                logger.LogInformation(
                    "インデックス最適化: {Table}.{Index} ({Pct:F1}%)",
                    idx.TableName, idx.IndexName, idx.FragmentationPct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "インデックス最適化失敗: {Index}", idx.IndexName);
            }
        }
    }
}
