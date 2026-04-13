// WinConflu.NET — types.ts
// Blazor ↔ Tiptap 間の型定義

/** Tiptap が保存する ProseMirror JSON の最上位型 */
export interface ProseMirrorDoc {
  type: 'doc';
  content: ProseMirrorNode[];
}

export interface ProseMirrorNode {
  type: string;
  attrs?:   Record<string, unknown>;
  content?: ProseMirrorNode[];
  marks?:   ProseMirrorMark[];
  text?:    string;
}

export interface ProseMirrorMark {
  type:   string;
  attrs?: Record<string, unknown>;
}

/** スラッシュコマンドのアイテム */
export interface SlashCommandItem {
  title:       string;
  description: string;
  icon:        string;
  command:     (editor: unknown) => void;
}

/** @メンション候補（Blazor から渡される） */
export interface MentionUser {
  id:          string;
  displayName: string;
  department?: string;
  photoBase64?: string;
}

/** [[ISSUE-XXX]] ブロックの表示データ（Blazor から渡される） */
export interface IssueBadgeData {
  issueId:     number;
  key:         string;   // "WCN-12"
  title:       string;
  status:      string;
  priority:    string;
  assignee?:   string;
}

/** Blazor に公開する C# 呼び出し用 DotNet reference の型 */
export interface DotNetHelper {
  invokeMethodAsync<T>(method: string, ...args: unknown[]): Promise<T>;
}

/** Bubble Menu で表示するボタン設定 */
export interface BubbleMenuButton {
  name:    string;
  icon:    string;
  action:  string;   // 'bold' | 'italic' | 'link' | ...
  active?: boolean;
}
