// ============================================================
// WinConflu.NET — BoardService (完全版)
// IssueType / エピック階層 / ワークフロー統合 / Outlook 同期
// ============================================================

using Microsoft.EntityFrameworkCore;
using WinConflu.Data;
using WinConflu.Models;
using WinConflu.Services.Graph;

namespace WinConflu.Services;

public interface IBoardService
{
    // ── プロジェクト ──────────────────────────────────────────
    Task<List<Project>>  GetProjectsAsync(string userSid, CancellationToken ct = default);
    Task<Project>        GetProjectAsync(int id, CancellationToken ct = default);
    Task<Project>        CreateProjectAsync(CreateProjectRequest req, string authorSid, CancellationToken ct = default);

    // ── チケット ──────────────────────────────────────────────
    Task<List<Issue>>    GetIssuesAsync(int projectId, IssueStatus? status = null, IssueType? type = null, CancellationToken ct = default);
    Task<List<Issue>>    GetEpicsAsync(int projectId, CancellationToken ct = default);
    Task<List<Issue>>    GetChildrenAsync(int parentIssueId, CancellationToken ct = default);
    Task<Issue>          GetIssueAsync(int id, CancellationToken ct = default);
    Task<Issue>          CreateIssueAsync(CreateIssueRequest req, string authorSid, CancellationToken ct = default);
    Task<Issue>          UpdateIssueAsync(int id, UpdateIssueRequest req, string authorSid, CancellationToken ct = default);

    /// <summary>ワークフロー検証付きステータス遷移</summary>
    Task<Issue>          MoveIssueAsync(int id, IssueStatus newStatus, string authorSid, string? comment = null, CancellationToken ct = default);
    Task                 DeleteIssueAsync(int id, string authorSid, CancellationToken ct = default);

    // ── 関連 ──────────────────────────────────────────────────
    Task                 LinkPageAsync(int issueId, int pageId, CancellationToken ct = default);
    Task                 UnlinkPageAsync(int issueId, int pageId, CancellationToken ct = default);
    Task                 AddDependencyAsync(int sourceId, int targetId, DependencyType type, CancellationToken ct = default);
    Task                 RemoveDependencyAsync(int dependencyId, CancellationToken ct = default);

    // ── ビュー用集計 ─────────────────────────────────────────
    Task<List<IssueStatus>> GetAllowedStatusesAsync(int issueId, string userSid, CancellationToken ct = default);
    Task<int>               GetProgressAsync(int issueId, CancellationToken ct = default);
}

// ── リクエスト DTO ────────────────────────────────────────────
public record CreateProjectRequest(string Name, string Prefix, string? Description, string AdGroupOwnerSid);

public record CreateIssueRequest(
    int ProjectId,
    string Title,
    string? Description,
    IssueType Type,
    IssuePriority Priority,
    string? AssigneeSid,
    DateTimeOffset? DueDate,
    int? ParentIssueId,
    List<int>? LinkedPageIds);

public record UpdateIssueRequest(
    string Title,
    string? Description,
    IssueType Type,
    IssuePriority Priority,
    string? AssigneeSid,
    DateTimeOffset? DueDate,
    int? ParentIssueId);

// ────────────────────────────────────────────────────────────

public class BoardService(
    AppDbContext db,
    IWorkflowService workflow,
    ICalendarService calendar,
    IAuditService audit) : IBoardService
{
    // ── プロジェクト ──────────────────────────────────────────

    public async Task<List<Project>> GetProjectsAsync(string userSid, CancellationToken ct = default)
        => await db.Projects.Where(p => !p.IsArchived).OrderBy(p => p.Name).ToListAsync(ct);

    public async Task<Project> GetProjectAsync(int id, CancellationToken ct = default)
        => await db.Projects
            .Include(p => p.Issues.Where(i => !i.IsDeleted))
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new InvalidOperationException($"プロジェクト {id} が見つかりません。");

    public async Task<Project> CreateProjectAsync(
        CreateProjectRequest req, string authorSid, CancellationToken ct = default)
    {
        if (await db.Projects.AnyAsync(p => p.Prefix == req.Prefix.ToUpper(), ct))
            throw new InvalidOperationException($"プレフィックス {req.Prefix} はすでに使用されています。");

        var project = new Project
        {
            Name = req.Name, Prefix = req.Prefix.ToUpper(),
            Description = req.Description, AdGroupOwnerSid = req.AdGroupOwnerSid
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Create", "Project", project.Id, authorSid, null, req.Name);
        return project;
    }

    // ── チケット取得 ──────────────────────────────────────────

    public async Task<List<Issue>> GetIssuesAsync(
        int projectId, IssueStatus? status = null, IssueType? type = null, CancellationToken ct = default)
    {
        IQueryable<Issue> q = db.Issues
            .Where(i => i.ProjectId == projectId && !i.IsDeleted && i.ParentIssueId == null)
            .Include(i => i.Labels)
            .Include(i => i.SubIssues.Where(s => !s.IsDeleted));

        if (status.HasValue) q = q.Where(i => i.Status == status.Value);
        if (type.HasValue)   q = q.Where(i => i.Type == type.Value);

        return await q.OrderBy(i => i.Type).ThenBy(i => i.IssueNumber).ToListAsync(ct);
    }

    public async Task<List<Issue>> GetEpicsAsync(int projectId, CancellationToken ct = default)
        => await db.Issues
            .Where(i => i.ProjectId == projectId && i.Type == IssueType.Epic && !i.IsDeleted)
            .Include(i => i.SubIssues.Where(s => !s.IsDeleted))
            .OrderBy(i => i.IssueNumber)
            .ToListAsync(ct);

    public async Task<List<Issue>> GetChildrenAsync(int parentIssueId, CancellationToken ct = default)
        => await db.Issues
            .Where(i => i.ParentIssueId == parentIssueId && !i.IsDeleted)
            .Include(i => i.Labels)
            .OrderBy(i => i.IssueNumber)
            .ToListAsync(ct);

    public async Task<Issue> GetIssueAsync(int id, CancellationToken ct = default)
        => await db.Issues
            .Include(i => i.LinkedPages)
            .Include(i => i.Labels)
            .Include(i => i.Comments)
            .Include(i => i.SubIssues.Where(s => !s.IsDeleted))
            .Include(i => i.BlockedBy).ThenInclude(d => d.SourceIssue)
            .Include(i => i.Blocks).ThenInclude(d => d.TargetIssue)
            .FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted, ct)
            ?? throw new InvalidOperationException($"チケット {id} が見つかりません。");

    // ── チケット作成 ──────────────────────────────────────────

    public async Task<Issue> CreateIssueAsync(
        CreateIssueRequest req, string authorSid, CancellationToken ct = default)
    {
        // エピックのサブタスクはタイプを SubTask に強制
        if (req.ParentIssueId.HasValue)
        {
            var parent = await db.Issues.FindAsync([req.ParentIssueId.Value], ct);
            if (parent?.Type == IssueType.Epic && req.Type == IssueType.Task)
            {
                // エピック配下はそのままでもOK（Story/SubTask 両方許容）
            }
        }

        var nextNumber = (await db.Issues
            .Where(i => i.ProjectId == req.ProjectId)
            .MaxAsync(i => (int?)i.IssueNumber, ct) ?? 0) + 1;

        var issue = new Issue
        {
            ProjectId     = req.ProjectId,
            IssueNumber   = nextNumber,
            Title         = req.Title,
            Description   = req.Description,
            Type          = req.Type,
            Priority      = req.Priority,
            AssigneeSid   = req.AssigneeSid,
            DueDate       = req.DueDate,
            ParentIssueId = req.ParentIssueId,
            ReporterSid   = authorSid
        };

        if (req.LinkedPageIds?.Any() == true)
        {
            var pages = await db.Pages.Where(p => req.LinkedPageIds.Contains(p.Id)).ToListAsync(ct);
            issue.LinkedPages = pages;
        }

        db.Issues.Add(issue);
        await db.SaveChangesAsync(ct);

        if (req.DueDate.HasValue && !string.IsNullOrEmpty(req.AssigneeSid))
            await SyncToOutlookAsync(issue, ct);

        await audit.LogAsync("Create", "Issue", issue.Id, authorSid, null, $"[{req.Type}] {req.Title}");
        return issue;
    }

    // ── チケット更新 ──────────────────────────────────────────

    public async Task<Issue> UpdateIssueAsync(
        int id, UpdateIssueRequest req, string authorSid, CancellationToken ct = default)
    {
        var issue = await db.Issues.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"チケット {id} が見つかりません。");

        var oldTitle = issue.Title;
        issue.Title         = req.Title;
        issue.Description   = req.Description;
        issue.Type          = req.Type;
        issue.Priority      = req.Priority;
        issue.AssigneeSid   = req.AssigneeSid;
        issue.DueDate       = req.DueDate;
        issue.ParentIssueId = req.ParentIssueId;
        issue.UpdatedAt     = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        if (req.DueDate.HasValue && !string.IsNullOrEmpty(req.AssigneeSid))
            await SyncToOutlookAsync(issue, ct);

        await audit.LogAsync("Update", "Issue", id, authorSid, oldTitle, req.Title);
        return issue;
    }

    // ── ワークフロー検証付きステータス遷移 ────────────────────

    public async Task<Issue> MoveIssueAsync(
        int id, IssueStatus newStatus, string authorSid,
        string? comment = null, CancellationToken ct = default)
    {
        // ワークフロー定義に従い遷移可否を検証（定義なければ自由遷移）
        await workflow.ValidateTransitionAsync(id, newStatus, authorSid, comment, ct);

        var issue = await db.Issues.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"チケット {id} が見つかりません。");

        var oldStatus = issue.Status;
        issue.Status    = newStatus;
        issue.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        // コメントが指定されていれば記録
        if (!string.IsNullOrWhiteSpace(comment))
        {
            db.Comments.Add(new Comment
            {
                RelatedType = "Issue",
                RelatedId   = id,
                Body        = $"[ステータス変更: {oldStatus} → {newStatus}] {comment}",
                AuthorSid   = authorSid
            });
            await db.SaveChangesAsync(ct);
        }

        // オートメーションエンジンを実行（子全完了 → 親自動完了 等）
        await workflow.RunAutomationsAsync(issue, oldStatus, ct);

        await audit.LogAsync("Move", "Issue", id, authorSid, oldStatus.ToString(), newStatus.ToString());
        return issue;
    }

    public async Task DeleteIssueAsync(int id, string authorSid, CancellationToken ct = default)
    {
        var issue = await db.Issues.FindAsync([id], ct)
            ?? throw new InvalidOperationException($"チケット {id} が見つかりません。");

        // サブタスクも連動削除
        var children = await db.Issues.Where(i => i.ParentIssueId == id).ToListAsync(ct);
        foreach (var child in children) child.IsDeleted = true;

        issue.IsDeleted = true;
        issue.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Delete", "Issue", id, authorSid, issue.Title, null);
    }

    // ── Wiki リンク ───────────────────────────────────────────

    public async Task LinkPageAsync(int issueId, int pageId, CancellationToken ct = default)
    {
        var issue = await db.Issues.Include(i => i.LinkedPages).FirstAsync(i => i.Id == issueId, ct);
        var page  = await db.Pages.FindAsync([pageId], ct)
            ?? throw new InvalidOperationException($"ページ {pageId} が見つかりません。");

        if (!issue.LinkedPages.Any(p => p.Id == pageId))
        {
            issue.LinkedPages.Add(page);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task UnlinkPageAsync(int issueId, int pageId, CancellationToken ct = default)
    {
        var issue = await db.Issues.Include(i => i.LinkedPages).FirstAsync(i => i.Id == issueId, ct);
        var page  = issue.LinkedPages.FirstOrDefault(p => p.Id == pageId);
        if (page is not null) { issue.LinkedPages.Remove(page); await db.SaveChangesAsync(ct); }
    }

    // ── 依存関係 ─────────────────────────────────────────────

    public async Task AddDependencyAsync(
        int sourceId, int targetId, DependencyType type, CancellationToken ct = default)
    {
        if (sourceId == targetId) throw new InvalidOperationException("自己依存は設定できません。");

        var exists = await db.IssueDependencies
            .AnyAsync(d => d.SourceIssueId == sourceId && d.TargetIssueId == targetId, ct);
        if (exists) return;

        db.IssueDependencies.Add(new IssueDependency
        {
            SourceIssueId = sourceId,
            TargetIssueId = targetId,
            Type          = type
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveDependencyAsync(int dependencyId, CancellationToken ct = default)
    {
        var dep = await db.IssueDependencies.FindAsync([dependencyId], ct)
            ?? throw new InvalidOperationException($"依存関係 {dependencyId} が見つかりません。");
        db.IssueDependencies.Remove(dep);
        await db.SaveChangesAsync(ct);
    }

    // ── 集計 ─────────────────────────────────────────────────

    public async Task<List<IssueStatus>> GetAllowedStatusesAsync(
        int issueId, string userSid, CancellationToken ct = default)
        => await workflow.GetAllowedNextStatusesAsync(issueId, userSid, ct);

    public async Task<int> GetProgressAsync(int issueId, CancellationToken ct = default)
    {
        var issue = await db.Issues.FindAsync([issueId], ct);
        if (issue == null) return 0;

        var children = await db.Issues
            .Where(i => i.ParentIssueId == issueId && !i.IsDeleted)
            .ToListAsync(ct);

        return WorkflowService.CalculateProgress(issue.Status, children);
    }

    // ── Outlook 予定表同期 ────────────────────────────────────

    private async Task SyncToOutlookAsync(Issue issue, CancellationToken ct)
    {
        try
        {
            await calendar.UpsertTaskAsync(
                userSid:     issue.AssigneeSid!,
                subject:     issue.Title,
                dueDate:     issue.DueDate!.Value,
                description: issue.Description,
                externalId:  $"wcn-issue-{issue.Id}",
                ct:          ct);

            issue.OutlookSynced = true;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Outlook sync failed for issue {issue.Id}: {ex.Message}");
        }
    }
}
