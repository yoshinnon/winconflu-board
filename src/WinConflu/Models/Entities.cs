// ============================================================
// WinConflu.NET — ドメインモデル
// ============================================================

using Microsoft.EntityFrameworkCore;

namespace WinConflu.Models;

// ── Wiki ────────────────────────────────────────────────────

/// <summary>Wikiページ（無限階層構造）</summary>
public class Page
{
    public int    Id          { get; set; }
    public string Title       { get; set; } = string.Empty;

    /// <summary>
    /// 保存コンテンツ本体。
    /// ContentFormat = "json"  → ProseMirror JSON 文字列
    /// ContentFormat = "markdown" → 既存の Markdown 文字列（移行済みまで混在）
    /// </summary>
    public string Content     { get; set; } = string.Empty;

    /// <summary>コンテンツ形式識別子。"json" | "markdown"</summary>
    public string ContentFormat { get; set; } = "markdown";

    /// <summary>全文検索用プレーンテキストキャッシュ（ProseMirrorService.ToPlainText の出力）</summary>
    public string ContentText { get; set; } = string.Empty;

    /// <summary>HierarchyId による階層パス（例: /1/3/2/）</summary>
    public HierarchyId Path  { get; set; } = HierarchyId.GetRoot();

    /// <summary>アクセス制御用 ADグループ SID</summary>
    public string? AdGroupSid { get; set; }

    public string? Slug       { get; set; }   // URL フレンドリーな識別子
    public bool    IsDeleted  { get; set; }
    public string  CreatedBy  { get; set; } = string.Empty;
    public string  UpdatedBy  { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    // ナビゲーション
    public ICollection<Revision>   Revisions    { get; set; } = [];
    public ICollection<Attachment> Attachments  { get; set; } = [];
    public ICollection<Comment>    Comments     { get; set; } = [];
    public ICollection<Issue>      LinkedIssues { get; set; } = [];
}

/// <summary>ページ更新履歴</summary>
public class Revision
{
    public int    Id            { get; set; }
    public int    PageId        { get; set; }
    public string Content       { get; set; } = string.Empty;
    public string ContentFormat { get; set; } = "markdown";  // "json" | "markdown"
    public string AuthorSid    { get; set; } = string.Empty;
    public string? Comment     { get; set; }
    public int    Version      { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Page Page { get; set; } = null!;
}

// ── Boards ──────────────────────────────────────────────────

/// <summary>プロジェクト（ボード単位）</summary>
public class Project
{
    public int    Id               { get; set; }
    public string Name             { get; set; } = string.Empty;
    public string Prefix           { get; set; } = string.Empty;  // 例: "WCN"
    public string? Description     { get; set; }
    public string AdGroupOwnerSid  { get; set; } = string.Empty;
    public bool   IsArchived       { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Issue> Issues { get; set; } = [];
}

/// <summary>タスク / チケット</summary>
public class Issue
{
    public int           Id            { get; set; }
    public int           ProjectId     { get; set; }
    public int           IssueNumber   { get; set; }   // プロジェクト内連番（例: WCN-12）
    public string        Title         { get; set; } = string.Empty;
    public string?       Description   { get; set; }
    public IssueType     Type          { get; set; } = IssueType.Task;
    public IssueStatus   Status        { get; set; } = IssueStatus.Todo;
    public IssuePriority Priority      { get; set; } = IssuePriority.Medium;
    public string?       AssigneeSid   { get; set; }
    public string?       ReporterSid   { get; set; }
    public DateTimeOffset? DueDate     { get; set; }
    public DateTimeOffset? StartDate   { get; set; }   // ロードマップ用開始日

    /// <summary>エピック / 親チケットの ID（階層構造）</summary>
    public int?          ParentIssueId { get; set; }

    /// <summary>ストーリーポイント（スクラム見積もり）</summary>
    public int?          StoryPoints   { get; set; }

    /// <summary>エピック表示色（カンバン・ロードマップでの色分け）</summary>
    public string?       EpicColor     { get; set; }

    /// <summary>完了率 0〜100（サブタスク集計または手動入力）</summary>
    public int           Progress      { get; set; }

    public bool          IsDeleted     { get; set; }
    public bool          OutlookSynced { get; set; }
    public DateTimeOffset CreatedAt    { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt    { get; set; } = DateTimeOffset.UtcNow;

    public Project                     Project      { get; set; } = null!;
    public Issue?                      ParentIssue  { get; set; }
    public ICollection<Issue>          SubIssues    { get; set; } = [];   // 子チケット
    public ICollection<Attachment>     Attachments  { get; set; } = [];
    public ICollection<Comment>        Comments     { get; set; } = [];
    public ICollection<Page>           LinkedPages  { get; set; } = [];
    public ICollection<IssueLabel>     Labels       { get; set; } = [];
    public ICollection<IssueDependency> BlockedBy   { get; set; } = [];
    public ICollection<IssueDependency> Blocks      { get; set; } = [];
}

public enum IssueStatus
{
    Todo,
    Doing,
    Done,
    Verified
}

public enum IssuePriority
{
    Low,
    Medium,
    High,
    Critical
}

// IssueType は Models/BoardsExtended.cs で定義

/// <summary>チケットラベル</summary>
public class IssueLabel
{
    public int    Id       { get; set; }
    public int    IssueId  { get; set; }
    public string Name     { get; set; } = string.Empty;
    public string Color    { get; set; } = "#378ADD";
    public Issue  Issue    { get; set; } = null!;
}

// ── 共通 ────────────────────────────────────────────────────

/// <summary>添付ファイル（Page / Issue どちらにもリンク可能）</summary>
public class Attachment
{
    public int     Id          { get; set; }
    public int     RelatedId   { get; set; }          // PageId or IssueId
    public string  RelatedType { get; set; } = string.Empty; // "Page" or "Issue"
    public string  FileName    { get; set; } = string.Empty;
    public string  BlobUrl     { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long    SizeBytes   { get; set; }
    public string  UploadedBy  { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>操作監査ログ（誰が・いつ・何をしたか）</summary>
public class AuditLog
{
    public long   Id         { get; set; }
    public string UserSid    { get; set; } = string.Empty;
    public string Action     { get; set; } = string.Empty;  // "View" / "Create" / "Update" / "Delete"
    public string EntityType { get; set; } = string.Empty;
    public int?   EntityId   { get; set; }
    public string? OldValue  { get; set; }
    public string? NewValue  { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>コメント（Page / Issue に添付）</summary>
public class Comment
{
    public int    Id          { get; set; }
    public int    RelatedId   { get; set; }
    public string RelatedType { get; set; } = string.Empty;
    public string Body        { get; set; } = string.Empty;
    public string AuthorSid   { get; set; } = string.Empty;
    public bool   IsDeleted   { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
