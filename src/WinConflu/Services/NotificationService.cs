// ============================================================
// WinConflu.NET — NotificationService
// アプリ内通知テーブル + Hangfire バックグラウンドジョブ
// ============================================================

using Hangfire;
using Microsoft.EntityFrameworkCore;
using WinConflu.Data;
using WinConflu.Models;

namespace WinConflu.Services;

// ────────────────────────────────────────────────────────────
// 通知エンティティ（Entities.cs に追記相当 — ここで定義）
// ────────────────────────────────────────────────────────────

public class AppNotification
{
    public long   Id          { get; set; }
    public string RecipientSid{ get; set; } = string.Empty;
    public string Title       { get; set; } = string.Empty;
    public string Body        { get; set; } = string.Empty;
    public NotificationKind Kind { get; set; }
    public string? LinkUrl    { get; set; }
    public bool   IsRead      { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum NotificationKind
{
    IssueAssigned,          // チケットが自分にアサインされた
    IssueStatusChanged,     // 担当チケットのステータスが変わった
    IssueCommented,         // 担当/注目チケットにコメントが付いた
    DueDateApproaching,     // 期限が24時間以内
    DueDateOverdue,         // 期限超過
    AnnotationReplied,      // 自分の注釈に返信が付いた
    PageUpdated,            // お気に入りページが更新された
    WorkflowBlocked,        // ワークフローのブロッカーが解除された
    MentionedInComment      // コメントで @メンション された
}

// ────────────────────────────────────────────────────────────
// INotificationService
// ────────────────────────────────────────────────────────────

public interface INotificationService
{
    // ── アプリ内通知 CRUD ─────────────────────────────────────
    Task<List<AppNotification>> GetUnreadAsync(string userSid, CancellationToken ct = default);
    Task<int>                   GetUnreadCountAsync(string userSid, CancellationToken ct = default);
    Task                        MarkReadAsync(long id, CancellationToken ct = default);
    Task                        MarkAllReadAsync(string userSid, CancellationToken ct = default);

    // ── 通知送信（直接呼び出し） ─────────────────────────────
    Task NotifyAsync(string recipientSid, string title, string body,
                     NotificationKind kind, string? linkUrl = null,
                     CancellationToken ct = default);

    // ── Hangfire ジョブのスケジューリング ────────────────────
    void ScheduleDueDateReminders();
    void ScheduleOverdueCheck();
}

// ────────────────────────────────────────────────────────────
// NotificationService 実装
// ────────────────────────────────────────────────────────────

public class NotificationService(
    AppDbContext db,
    IBackgroundJobClient jobs,
    ILogger<NotificationService> logger) : INotificationService
{
    // ── アプリ内通知 ─────────────────────────────────────────

    public async Task<List<AppNotification>> GetUnreadAsync(
        string userSid, CancellationToken ct = default)
        => await db.AppNotifications
            .Where(n => n.RecipientSid == userSid && !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

    public async Task<int> GetUnreadCountAsync(
        string userSid, CancellationToken ct = default)
        => await db.AppNotifications
            .CountAsync(n => n.RecipientSid == userSid && !n.IsRead, ct);

    public async Task MarkReadAsync(long id, CancellationToken ct = default)
    {
        var n = await db.AppNotifications.FindAsync([id], ct);
        if (n is not null) { n.IsRead = true; await db.SaveChangesAsync(ct); }
    }

    public async Task MarkAllReadAsync(string userSid, CancellationToken ct = default)
    {
        await db.AppNotifications
            .Where(n => n.RecipientSid == userSid && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }

    // ── 通知送信 ─────────────────────────────────────────────

    public async Task NotifyAsync(
        string recipientSid, string title, string body,
        NotificationKind kind, string? linkUrl = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(recipientSid)) return;

        db.AppNotifications.Add(new AppNotification
        {
            RecipientSid = recipientSid,
            Title        = title,
            Body         = body,
            Kind         = kind,
            LinkUrl      = linkUrl
        });
        await db.SaveChangesAsync(ct);

        logger.LogDebug("通知送信: {Kind} → {Recipient}", kind, recipientSid);
    }

    // ── Hangfire ジョブ登録 ──────────────────────────────────

    public void ScheduleDueDateReminders()
    {
        // 毎時 0 分に「24時間以内期限」のチェックを実行
        RecurringJob.AddOrUpdate(
            "due-date-reminder",
            () => CheckDueDateReminderJob(),
            Cron.Hourly);
    }

    public void ScheduleOverdueCheck()
    {
        // 毎朝 9 時に期限超過チェック
        RecurringJob.AddOrUpdate(
            "overdue-check",
            () => CheckOverdueJob(),
            "0 9 * * *");
    }

    // ── Hangfire ジョブ本体（public = Hangfire が直接 invoke できる） ──

    [AutomaticRetry(Attempts = 3)]
    public async Task CheckDueDateReminderJob()
    {
        logger.LogInformation("期限リマインダーチェック開始");

        var threshold = DateTimeOffset.UtcNow.AddHours(24);
        var issues = await db.Issues
            .Where(i => !i.IsDeleted
                     && i.DueDate.HasValue
                     && i.DueDate <= threshold
                     && i.DueDate > DateTimeOffset.UtcNow
                     && i.Status != IssueStatus.Done
                     && i.Status != IssueStatus.Verified
                     && i.AssigneeSid != null)
            .Include(i => i.Project)
            .ToListAsync();

        foreach (var issue in issues)
        {
            var projectPrefix = issue.Project.Prefix;
            var remainHours   = (int)(issue.DueDate!.Value - DateTimeOffset.UtcNow).TotalHours;

            await NotifyAsync(
                issue.AssigneeSid!,
                $"期限が近づいています: {projectPrefix}-{issue.IssueNumber}",
                $"「{issue.Title}」の期限まで残り約 {remainHours} 時間です。",
                NotificationKind.DueDateApproaching,
                $"/boards/issue/{issue.Id}");
        }

        logger.LogInformation("期限リマインダーチェック完了: {Count} 件", issues.Count);
    }

    [AutomaticRetry(Attempts = 3)]
    public async Task CheckOverdueJob()
    {
        logger.LogInformation("期限超過チェック開始");

        var issues = await db.Issues
            .Where(i => !i.IsDeleted
                     && i.DueDate.HasValue
                     && i.DueDate < DateTimeOffset.UtcNow
                     && i.Status != IssueStatus.Done
                     && i.Status != IssueStatus.Verified
                     && i.AssigneeSid != null)
            .Include(i => i.Project)
            .ToListAsync();

        foreach (var issue in issues)
        {
            var daysOver = (int)(DateTimeOffset.UtcNow - issue.DueDate!.Value).TotalDays;
            await NotifyAsync(
                issue.AssigneeSid!,
                $"期限超過: {issue.Project.Prefix}-{issue.IssueNumber}",
                $"「{issue.Title}」の期限が {daysOver} 日超過しています。",
                NotificationKind.DueDateOverdue,
                $"/boards/issue/{issue.Id}");
        }

        logger.LogInformation("期限超過チェック完了: {Count} 件", issues.Count);
    }
}

// ────────────────────────────────────────────────────────────
// NotificationHub — SignalR でリアルタイムに通知数をプッシュ
// ────────────────────────────────────────────────────────────

public class NotificationHub : Microsoft.AspNetCore.SignalR.Hub
{
    // クライアント側: connection.on("NewNotification", count => ...)
    public async Task BroadcastUnreadCount(string userSid, int count)
        => await Clients.User(userSid).SendAsync("NewNotification", count);
}
