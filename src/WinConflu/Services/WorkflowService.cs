// ============================================================
// WinConflu.NET — WorkflowService
// ステータス遷移ルール検証 + オートメーションエンジン
// ============================================================

using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WinConflu.Data;
using WinConflu.Models;

namespace WinConflu.Services;public interface IWorkflowService
{
    /// <summary>プロジェクトのデフォルトワークフローを取得（なければ汎用ルールを返す）</summary>
    Task<WorkflowDefinition?>       GetDefaultAsync(int projectId, CancellationToken ct = default);

    /// <summary>遷移が許可されているか検証し、許可されない場合は例外を投げる</summary>
    Task                            ValidateTransitionAsync(int issueId, IssueStatus toStatus, string userSid, string? comment, CancellationToken ct = default);

    /// <summary>遷移後に自動化ルールを評価・実行する</summary>
    Task                            RunAutomationsAsync(Issue issue, IssueStatus fromStatus, CancellationToken ct = default);

    Task<WorkflowDefinition>        CreateAsync(CreateWorkflowRequest req, string authorSid, CancellationToken ct = default);
    Task<WorkflowDefinition>        AddTransitionAsync(int workflowId, AddTransitionRequest req, CancellationToken ct = default);
    Task                            RemoveTransitionAsync(int transitionId, CancellationToken ct = default);

    /// <summary>カンバン UI に渡す「次に移行できるステータス一覧」</summary>
    Task<List<IssueStatus>>         GetAllowedNextStatusesAsync(int issueId, string userSid, CancellationToken ct = default);
}

public record CreateWorkflowRequest(int ProjectId, string Name, bool IsDefault);
public record AddTransitionRequest(
    IssueStatus FromStatus, IssueStatus ToStatus,
    string? RequiredRole, bool RequireComment,
    string? AutomationJson);

public class WorkflowService(
    AppDbContext db,
    IAuditService audit,
    IAdGroupService adGroup,
    INotificationService notif,
    ILogger<WorkflowService> logger) : IWorkflowService
{
    // ── ワークフロー取得 ──────────────────────────────────────

    public async Task<WorkflowDefinition?> GetDefaultAsync(
        int projectId, CancellationToken ct = default)
        => await db.WorkflowDefinitions
            .Include(w => w.Transitions)
            .FirstOrDefaultAsync(w => w.ProjectId == projectId && w.IsDefault, ct);

    // ── 遷移バリデーション ────────────────────────────────────

    public async Task ValidateTransitionAsync(
        int issueId, IssueStatus toStatus,
        string userSid, string? comment,
        CancellationToken ct = default)
    {
        var issue = await db.Issues.FindAsync([issueId], ct)
            ?? throw new InvalidOperationException($"チケット {issueId} が見つかりません。");

        var workflow = await GetDefaultAsync(issue.ProjectId, ct);

        // ワークフロー定義がない場合は制限なし（フリー遷移）
        if (workflow == null) return;

        var transition = workflow.Transitions
            .FirstOrDefault(t => t.FromStatus == issue.Status && t.ToStatus == toStatus);

        if (transition == null)
            throw new WorkflowViolationException(
                $"ステータス '{issue.Status}' から '{toStatus}' への遷移は許可されていません。");

        // ロール検証（AD グループで確認）
        if (!string.IsNullOrEmpty(transition.RequiredRole))
        {
            var inGroup = await adGroup.IsInGroupAsync(userSid, transition.RequiredRole, ct);
            if (!inGroup)
                throw new WorkflowViolationException(
                    $"この遷移にはロール '{transition.RequiredRole}' が必要です。");
        }

        // コメント必須チェック
        if (transition.RequireComment && string.IsNullOrWhiteSpace(comment))
            throw new WorkflowViolationException(
                $"'{issue.Status}' → '{toStatus}' の遷移にはコメントが必要です。");
    }

    // ── オートメーションエンジン ──────────────────────────────

    public async Task RunAutomationsAsync(
        Issue issue, IssueStatus fromStatus, CancellationToken ct = default)
    {
        var workflow = await GetDefaultAsync(issue.ProjectId, ct);
        if (workflow == null) return;

        // 今回の遷移に紐づくオートメーションを取得
        var automation = workflow.Transitions
            .Where(t => t.FromStatus == fromStatus && t.ToStatus == issue.Status
                     && t.AutomationJson != null)
            .Select(t => TryParseAutomation(t.AutomationJson!))
            .FirstOrDefault(a => a != null);

        if (automation == null) return;

        switch (automation.Type)
        {
            case "AllSubTasksDone":
                await AutoCompleteParentAsync(issue, ct);
                break;

            case "DueDateApproaching":
                if (!string.IsNullOrEmpty(issue.AssigneeSid))
                    await notif.NotifyAsync(
                        issue.AssigneeSid,
                        $"期限が近づいています: Issue {issue.IssueNumber}",
                        $"「{issue.Title}」の期限が近づいています。",
                        NotificationKind.DueDateApproaching,
                        $"/boards/issue/{issue.Id}");
                break;

            case "AssignToReporter":
                if (string.IsNullOrEmpty(issue.AssigneeSid) && issue.ReporterSid != null)
                {
                    issue.AssigneeSid = issue.ReporterSid;
                    await db.SaveChangesAsync(ct);
                    logger.LogInformation("担当者を報告者に自動設定: Issue {Id}", issue.Id);
                }
                break;
        }

        // 子チケットが全て Done/Verified になった場合に親を自動完了
        await AutoCompleteParentIfAllChildrenDoneAsync(issue, ct);
    }

    /// <summary>
    /// 全サブタスクが Done/Verified になった場合に親チケットを自動 Done に変更。
    /// </summary>
    private async Task AutoCompleteParentAsync(Issue issue, CancellationToken ct)
    {
        if (issue.ParentIssueId == null) return;

        var allSiblings = await db.Issues
            .Where(i => i.ParentIssueId == issue.ParentIssueId && !i.IsDeleted)
            .ToListAsync(ct);

        bool allDone = allSiblings.All(i =>
            i.Status == IssueStatus.Done || i.Status == IssueStatus.Verified);

        if (!allDone) return;

        var parent = await db.Issues.FindAsync([issue.ParentIssueId.Value], ct);
        if (parent == null || parent.Status == IssueStatus.Done) return;

        parent.Status    = IssueStatus.Done;
        parent.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("AutoTransition", "Issue", parent.Id,
            "system", IssueStatus.Doing.ToString(), IssueStatus.Done.ToString());

        logger.LogInformation("子チケット全完了により親 {Id} を自動完了", parent.Id);
    }

    private async Task AutoCompleteParentIfAllChildrenDoneAsync(Issue issue, CancellationToken ct)
    {
        if (issue.ParentIssueId != null)
            await AutoCompleteParentAsync(issue, ct);
    }

    // ── Progress 自動集計 ─────────────────────────────────────

    public static int CalculateProgress(IssueStatus status, List<Issue> subIssues)
    {
        if (subIssues.Count == 0)
        {
            return status switch
            {
                IssueStatus.Verified => 100,
                IssueStatus.Done     => 90,
                IssueStatus.Doing    => 50,
                _                    => 0
            };
        }

        var done = subIssues.Count(i =>
            i.Status == IssueStatus.Done || i.Status == IssueStatus.Verified);
        return (int)Math.Round(done * 100.0 / subIssues.Count);
    }

    // ── ワークフロー CRUD ─────────────────────────────────────

    public async Task<WorkflowDefinition> CreateAsync(
        CreateWorkflowRequest req, string authorSid, CancellationToken ct = default)
    {
        // IsDefault を true にする場合は既存の Default を解除
        if (req.IsDefault)
        {
            var existing = await db.WorkflowDefinitions
                .Where(w => w.ProjectId == req.ProjectId && w.IsDefault)
                .ToListAsync(ct);
            existing.ForEach(w => w.IsDefault = false);
        }

        var wf = new WorkflowDefinition
        {
            ProjectId = req.ProjectId,
            Name      = req.Name,
            IsDefault = req.IsDefault
        };

        db.WorkflowDefinitions.Add(wf);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Create", "WorkflowDefinition", wf.Id, authorSid, null, req.Name);
        return wf;
    }

    public async Task<WorkflowDefinition> AddTransitionAsync(
        int workflowId, AddTransitionRequest req, CancellationToken ct = default)
    {
        var wf = await db.WorkflowDefinitions
            .Include(w => w.Transitions)
            .FirstOrDefaultAsync(w => w.Id == workflowId, ct)
            ?? throw new InvalidOperationException($"ワークフロー {workflowId} が見つかりません。");

        wf.Transitions.Add(new WorkflowTransition
        {
            WorkflowId     = workflowId,
            FromStatus     = req.FromStatus,
            ToStatus       = req.ToStatus,
            RequiredRole   = req.RequiredRole,
            RequireComment = req.RequireComment,
            AutomationJson = req.AutomationJson
        });

        await db.SaveChangesAsync(ct);
        return wf;
    }

    public async Task RemoveTransitionAsync(int transitionId, CancellationToken ct = default)
    {
        var t = await db.WorkflowTransitions.FindAsync([transitionId], ct)
            ?? throw new InvalidOperationException($"遷移 {transitionId} が見つかりません。");
        db.WorkflowTransitions.Remove(t);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<IssueStatus>> GetAllowedNextStatusesAsync(
        int issueId, string userSid, CancellationToken ct = default)
    {
        var issue = await db.Issues.FindAsync([issueId], ct);
        if (issue == null) return [];

        var workflow = await GetDefaultAsync(issue.ProjectId, ct);

        // ワークフロー未設定 → 全ステータスへの遷移を許可
        if (workflow == null)
            return Enum.GetValues<IssueStatus>()
                .Where(s => s != issue.Status)
                .ToList();

        return workflow.Transitions
            .Where(t => t.FromStatus == issue.Status)
            .Select(t => t.ToStatus)
            .Distinct()
            .ToList();
    }

    private static WorkflowAutomation? TryParseAutomation(string json)
    {
        try { return JsonSerializer.Deserialize<WorkflowAutomation>(json); }
        catch { return null; }
    }
}

/// <summary>ワークフロールール違反例外</summary>
public class WorkflowViolationException(string message) : InvalidOperationException(message);
