// ============================================================
// WinConflu.NET — テンプレート & インラインコメント モデル
// ============================================================

namespace WinConflu.Models;

// ────────────────────────────────────────────────────────────
// PageTemplate — ページテンプレート
// ────────────────────────────────────────────────────────────

/// <summary>
/// ページ新規作成時に選択できる定型フォーマット。
/// 「議事録」「仕様書」「日報」など。
/// </summary>
public class PageTemplate
{
    public int    Id          { get; set; }
    public string Name        { get; set; } = string.Empty;   // 表示名: "議事録"
    public string Description { get; set; } = string.Empty;   // 説明: "会議の記録に使用"
    public string Category    { get; set; } = string.Empty;   // カテゴリ: "会議" / "開発" / "管理"
    public string IconName    { get; set; } = "Description";  // MudBlazor アイコン名
    public string Content     { get; set; } = string.Empty;   // Markdown テンプレート本文

    /// <summary>
    /// テンプレート変数の定義（JSON 配列）。
    /// 例: [{"key":"DATE","label":"日付","defaultValue":"{{TODAY}}"}]
    /// </summary>
    public string VariablesJson { get; set; } = "[]";

    public bool   IsBuiltIn   { get; set; }  // true = 組み込み（削除不可）
    public bool   IsDeleted   { get; set; }
    public string CreatedBy   { get; set; } = string.Empty;
    public int    UsageCount  { get; set; }  // 使用回数（人気順ソート用）
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>テンプレート変数の定義（VariablesJson のデシリアライズ先）</summary>
public record TemplateVariable(
    string Key,           // プレースホルダーキー: "MEETING_DATE"
    string Label,         // UI 表示ラベル: "会議日"
    string DefaultValue,  // デフォルト値 ("{{TODAY}}" は動的解決)
    string InputType      // "text" / "date" / "select"
);

// ────────────────────────────────────────────────────────────
// InlineAnnotation — インラインコメント（テキスト選択注釈）
// ────────────────────────────────────────────────────────────

/// <summary>
/// ページ本文の特定テキスト範囲に紐づくコメントスレッド。
/// Confluence の「インラインコメント」に相当。
/// </summary>
public class InlineAnnotation
{
    public int    Id        { get; set; }
    public int    PageId    { get; set; }

    /// <summary>
    /// 選択テキストの文字オフセット（Markdown 本文内）。
    /// ページ更新時のずれ吸収は AnnotationAnchor で管理。
    /// </summary>
    public int    StartOffset { get; set; }
    public int    EndOffset   { get; set; }

    /// <summary>選択されたテキストのスナップショット（ずれ検出用）</summary>
    public string SelectedText { get; set; } = string.Empty;

    public AnnotationStatus Status { get; set; } = AnnotationStatus.Open;

    public bool   IsDeleted  { get; set; }
    public string CreatedBy  { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // ナビゲーション
    public Page                        Page     { get; set; } = null!;
    public ICollection<AnnotationReply> Replies { get; set; } = [];
}

public enum AnnotationStatus
{
    Open,      // 議論中
    Resolved,  // 解決済み
    Outdated   // 本文変更でアンカーが無効化
}

/// <summary>インラインコメントへの返信（スレッド形式）</summary>
public class AnnotationReply
{
    public int    Id           { get; set; }
    public int    AnnotationId { get; set; }
    public string Body         { get; set; } = string.Empty;
    public string AuthorSid    { get; set; } = string.Empty;
    public bool   IsDeleted    { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public InlineAnnotation Annotation { get; set; } = null!;
}
