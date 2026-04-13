// ============================================================
// WinConflu.NET — Boards 拡張モデル
// IssueType / エピック階層 / ワークフロー管理
// ============================================================

namespace WinConflu.Models;

// ────────────────────────────────────────────────────────────
// IssueType — チケット種別
// ────────────────────────────────────────────────────────────

public enum IssueType
{
    Task,       // 汎用タスク（デフォルト）
    Bug,        // バグ報告
    Story,      // ユーザーストーリー
    Epic,       // 複数ストーリーをまとめる大きな塊
    SubTask     // 親チケットの作業単位
}

// ────────────────────────────────────────────────────────────
// WorkflowDefinition — ワークフロー定義
// プロジェクトごとにカスタムワークフローを設定できる
// ────────────────────────────────────────────────────────────

/// <summary>プロジェクトに紐づくワークフロー設定</summary>
public class WorkflowDefinition
{
    public int    Id          { get; set; }
    public int    ProjectId   { get; set; }
    public string Name        { get; set; } = string.Empty;   // "開発ワークフロー"
    public bool   IsDefault   { get; set; }

    public Project              Project     { get; set; } = null!;
    public ICollection<WorkflowTransition> Transitions { get; set; } = [];
}

/// <summary>
/// ワークフロー遷移ルール。
/// FromStatus → ToStatus の移行に必要な条件を定義する。
/// </summary>
public class WorkflowTransition
{
    public int           Id                 { get; set; }
    public int           WorkflowId         { get; set; }
    public IssueStatus   FromStatus         { get; set; }
    public IssueStatus   ToStatus           { get; set; }

    /// <summary>移行に必要なロール（空 = 全員可）</summary>
    public string?       RequiredRole       { get; set; }

    /// <summary>移行時に必須コメントを要求するか</summary>
    public bool          RequireComment     { get; set; }

    /// <summary>
    /// 自動化トリガー（JSON）。
    /// 例: {"type":"AllSubTasksDone","action":"AutoTransition"}
    /// </summary>
    public string?       AutomationJson     { get; set; }

    public WorkflowDefinition Workflow      { get; set; } = null!;
}

/// <summary>ワークフロー遷移の自動化設定（AutomationJson のデシリアライズ先）</summary>
public record WorkflowAutomation(
    string Type,    // "AllSubTasksDone" / "DueDateApproaching" / "AssigneeEmpty"
    string Action   // "AutoTransition" / "Notify" / "AssignToReporter"
);

// ────────────────────────────────────────────────────────────
// IssueDependency — チケット間の依存関係
// ────────────────────────────────────────────────────────────

public class IssueDependency
{
    public int              Id             { get; set; }
    public int              SourceIssueId  { get; set; }
    public int              TargetIssueId  { get; set; }
    public DependencyType   Type           { get; set; }

    public Issue SourceIssue { get; set; } = null!;
    public Issue TargetIssue { get; set; } = null!;
}

public enum DependencyType
{
    Blocks,       // SourceIssue が TargetIssue をブロックしている
    IsBlockedBy,  // SourceIssue は TargetIssue によってブロックされている
    Relates,      // 関連（方向なし）
    Duplicates    // 重複
}
