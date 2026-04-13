// WinConflu.NET — editor.ts
// Tiptap エディタのメインエントリポイント
// Blazor JSInterop から呼び出される公開 API を window.wcnEditor に登録

import { Editor }        from '@tiptap/core';
import StarterKit        from '@tiptap/starter-kit';
import { Underline }     from '@tiptap/extension-underline';
import { TextAlign }     from '@tiptap/extension-text-align';
import { Highlight }     from '@tiptap/extension-highlight';
import { TextStyle }     from '@tiptap/extension-text-style';
import { Color }         from '@tiptap/extension-color';
import { Image }         from '@tiptap/extension-image';
import { Link }          from '@tiptap/extension-link';
import { Table }         from '@tiptap/extension-table';
import { TableRow }      from '@tiptap/extension-table-row';
import { TableCell }     from '@tiptap/extension-table-cell';
import { TableHeader }   from '@tiptap/extension-table-header';
import { TaskList }      from '@tiptap/extension-task-list';
import { TaskItem }      from '@tiptap/extension-task-item';
import { Placeholder }   from '@tiptap/extension-placeholder';
import { bindToolbarCommands, initCommandsApi } from './commands';
import { CharacterCount } from '@tiptap/extension-character-count';
import { Typography }    from '@tiptap/extension-typography';
import { Mention }       from '@tiptap/extension-mention';
import CodeBlockLowlight from '@tiptap/extension-code-block-lowlight';
import { createLowlight, all } from 'lowlight';
import tippy             from 'tippy.js';

import {
  InfoBox,
  IssueEmbed,
  createSlashCommandExtension,
  createIssuePatternExtension,
} from './extensions';
import type { DotNetHelper, MentionUser, SlashCommandItem } from './types';

// ── グローバル型拡張 ─────────────────────────────────────────
declare global {
  interface Window {
    wcnEditor: WcnEditorApi;
  }
}

// ── エディタインスタンス管理 ─────────────────────────────────
const _instances = new Map<string, Editor>();
const lowlight   = createLowlight(all);

// ────────────────────────────────────────────────────────────
// Blazor に公開する API
// ────────────────────────────────────────────────────────────
interface WcnEditorApi {
  init(elementId: string, dotnet: DotNetHelper, initialJson: string | null): void;
  destroy(elementId: string): void;
  getJson(elementId: string): string;
  setJson(elementId: string, json: string): void;
  setMarkdown(elementId: string, markdown: string): void;
  focus(elementId: string): void;
  insertImage(elementId: string, url: string, alt: string): void;
  embedIssue(elementId: string, issueKey: string, data: object): void;
  setEditable(elementId: string, editable: boolean): void;
}

// ────────────────────────────────────────────────────────────
// スラッシュコマンド定義
// ────────────────────────────────────────────────────────────
function getSlashCommands(editor: Editor): SlashCommandItem[] {
  return [
    {
      title: '見出し 1', description: '大見出し', icon: 'H1',
      command: () => editor.chain().focus().toggleHeading({ level: 1 }).run(),
    },
    {
      title: '見出し 2', description: '中見出し', icon: 'H2',
      command: () => editor.chain().focus().toggleHeading({ level: 2 }).run(),
    },
    {
      title: '見出し 3', description: '小見出し', icon: 'H3',
      command: () => editor.chain().focus().toggleHeading({ level: 3 }).run(),
    },
    {
      title: '箇条書き', description: '順序なしリスト', icon: '•',
      command: () => editor.chain().focus().toggleBulletList().run(),
    },
    {
      title: '番号リスト', description: '順序ありリスト', icon: '1.',
      command: () => editor.chain().focus().toggleOrderedList().run(),
    },
    {
      title: 'タスクリスト', description: 'チェックボックス付きリスト', icon: '☑',
      command: () => editor.chain().focus().toggleTaskList().run(),
    },
    {
      title: 'コードブロック', description: 'シンタックスハイライト付きコード', icon: '</>',
      command: () => editor.chain().focus().toggleCodeBlock().run(),
    },
    {
      title: '表', description: '3×3 のテーブルを挿入', icon: '⊞',
      command: () => editor.chain().focus().insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run(),
    },
    {
      title: '区切り線', description: '水平線を挿入', icon: '—',
      command: () => editor.chain().focus().setHorizontalRule().run(),
    },
    {
      title: 'Info ボックス', description: '情報パネル', icon: 'ℹ️',
      command: () => editor.chain().focus().insertContent({
        type: 'infoBox', attrs: { variant: 'info' },
        content: [{ type: 'paragraph' }],
      }).run(),
    },
    {
      title: 'Warning ボックス', description: '警告パネル', icon: '⚠️',
      command: () => editor.chain().focus().insertContent({
        type: 'infoBox', attrs: { variant: 'warning' },
        content: [{ type: 'paragraph' }],
      }).run(),
    },
    {
      title: 'Tip ボックス', description: 'ヒントパネル', icon: '💡',
      command: () => editor.chain().focus().insertContent({
        type: 'infoBox', attrs: { variant: 'tip' },
        content: [{ type: 'paragraph' }],
      }).run(),
    },
    {
      title: '引用', description: 'ブロック引用', icon: '"',
      command: () => editor.chain().focus().toggleBlockquote().run(),
    },
  ];
}

// ────────────────────────────────────────────────────────────
// スラッシュコマンド UI（tippy.js ポップアップ）
// ────────────────────────────────────────────────────────────
function createSlashPalette(editor: Editor, trigger: HTMLElement): void {
  const items  = getSlashCommands(editor);
  let selected = 0;

  const list = document.createElement('div');
  list.className = 'wcn-slash-palette';

  const render = () => {
    list.innerHTML = '';
    items.forEach((item, i) => {
      const btn = document.createElement('button');
      btn.className = `wcn-slash-item${i === selected ? ' selected' : ''}`;
      btn.innerHTML = `
        <span class="wcn-slash-icon">${item.icon}</span>
        <span class="wcn-slash-info">
          <span class="wcn-slash-title">${item.title}</span>
          <span class="wcn-slash-desc">${item.description}</span>
        </span>
      `;
      btn.addEventListener('mousedown', e => {
        e.preventDefault();
        // / を削除してコマンド実行
        const { from } = editor.state.selection;
        editor.chain().focus().deleteRange({ from: from - 1, to: from }).run();
        item.command(editor);
        instance.hide();
      });
      list.appendChild(btn);
    });
  };

  const instance = tippy(trigger, {
    content:   list,
    trigger:   'manual',
    placement: 'bottom-start',
    interactive: true,
    arrow:     false,
    theme:     'wcn-slash',
    onHide:    () => render(),
  });

  render();
  instance.show();

  // キーボードナビゲーション
  const handleKey = (e: KeyboardEvent) => {
    if (!instance.state.isVisible) return;
    if (e.key === 'ArrowDown') { selected = Math.min(selected + 1, items.length - 1); render(); e.preventDefault(); }
    if (e.key === 'ArrowUp')   { selected = Math.max(selected - 1, 0);               render(); e.preventDefault(); }
    if (e.key === 'Enter' || e.key === 'Tab') {
      e.preventDefault();
      const item = items[selected];
      const { from } = editor.state.selection;
      editor.chain().focus().deleteRange({ from: from - 1, to: from }).run();
      item.command(editor);
      instance.hide();
    }
    if (e.key === 'Escape') { instance.hide(); }
  };
  document.addEventListener('keydown', handleKey);
  instance.setProps({ onHide: () => { document.removeEventListener('keydown', handleKey); } });
}

// ────────────────────────────────────────────────────────────
// バブルメニュー（テキスト選択時のツールバー）
// ────────────────────────────────────────────────────────────
function createBubbleMenu(editor: Editor, container: HTMLElement): HTMLElement {
  const menu = document.createElement('div');
  menu.className = 'wcn-bubble-menu';
  menu.style.display = 'none';

  const buttons: Array<{ label: string; action: () => void; isActive: () => boolean }> = [
    { label: 'B',   action: () => editor.chain().focus().toggleBold().run(),       isActive: () => editor.isActive('bold') },
    { label: 'I',   action: () => editor.chain().focus().toggleItalic().run(),     isActive: () => editor.isActive('italic') },
    { label: 'U',   action: () => editor.chain().focus().toggleUnderline().run(),  isActive: () => editor.isActive('underline') },
    { label: 'S',   action: () => editor.chain().focus().toggleStrike().run(),     isActive: () => editor.isActive('strike') },
    { label: '`',   action: () => editor.chain().focus().toggleCode().run(),       isActive: () => editor.isActive('code') },
    { label: '🔗',  action: () => {
      const url = prompt('URL を入力してください:');
      if (url) editor.chain().focus().setLink({ href: url }).run();
    }, isActive: () => editor.isActive('link') },
    { label: 'H1',  action: () => editor.chain().focus().toggleHeading({ level: 1 }).run(), isActive: () => editor.isActive('heading', { level: 1 }) },
    { label: 'H2',  action: () => editor.chain().focus().toggleHeading({ level: 2 }).run(), isActive: () => editor.isActive('heading', { level: 2 }) },
    { label: '≡L',  action: () => editor.chain().focus().setTextAlign('left').run(),   isActive: () => editor.isActive({ textAlign: 'left' }) },
    { label: '≡C',  action: () => editor.chain().focus().setTextAlign('center').run(), isActive: () => editor.isActive({ textAlign: 'center' }) },
  ];

  buttons.forEach(btn => {
    const el = document.createElement('button');
    el.textContent = btn.label;
    el.className   = 'wcn-bubble-btn';
    el.title       = btn.label;
    el.addEventListener('mousedown', e => { e.preventDefault(); btn.action(); updateMenu(); });
    menu.appendChild(el);
  });

  const updateMenu = () => {
    Array.from(menu.querySelectorAll('.wcn-bubble-btn')).forEach((el, i) => {
      el.classList.toggle('active', buttons[i].isActive());
    });
  };

  // ProseMirror の selectionUpdate イベント経由でメニュー表示制御
  editor.on('selectionUpdate', ({ editor: e }) => {
    const { from, to, empty } = e.state.selection;
    if (empty) { menu.style.display = 'none'; return; }

    // 選択範囲の座標を取得
    const coords = e.view.coordsAtPos(from);
    const editorRect = container.getBoundingClientRect();
    menu.style.display = 'flex';
    menu.style.top  = `${coords.top - editorRect.top - 44}px`;
    menu.style.left = `${coords.left - editorRect.left}px`;
    updateMenu();
  });

  container.style.position = 'relative';
  container.appendChild(menu);
  return menu;
}

// ────────────────────────────────────────────────────────────
// Blazor 公開 API 実装
// ────────────────────────────────────────────────────────────
window.wcnEditor = {

  // ── エディタ初期化 ────────────────────────────────────────
  init(elementId: string, dotnet: DotNetHelper, initialJson: string | null) {
    const container = document.getElementById(elementId);
    if (!container) { console.error(`wcnEditor.init: #${elementId} not found`); return; }
    if (_instances.has(elementId)) { _instances.get(elementId)!.destroy(); }

    const editorEl = document.createElement('div');
    editorEl.className = 'wcn-tiptap-content';
    container.appendChild(editorEl);

    const editor = new Editor({
      element: editorEl,
      extensions: [
        StarterKit.configure({
          codeBlock: false,   // lowlight バージョンを使う
          history:   { depth: 100 },
        }),
        Underline,
        TextStyle,
        Color,
        Highlight.configure({ multicolor: true }),
        TextAlign.configure({ types: ['heading', 'paragraph'] }),
        Image.configure({ allowBase64: true }),
        Link.configure({ openOnClick: false, autolink: true }),
        Table.configure({ resizable: true }),
        TableRow, TableCell, TableHeader,
        TaskList, TaskItem.configure({ nested: true }),
        Typography,
        CharacterCount,
        CodeBlockLowlight.configure({ lowlight }),
        Placeholder.configure({ placeholder: 'ここに入力するか、/ でコマンドを呼び出してください...' }),
        InfoBox,
        IssueEmbed,
        createSlashCommandExtension(dotnet),
        createIssuePatternExtension(dotnet),

        // @メンション（ADユーザー検索）
        Mention.configure({
          HTMLAttributes: { class: 'wcn-mention' },
          suggestion: {
            items: async ({ query }: { query: string }) => {
              if (query.length < 1) return [];
              try {
                const users = await dotnet.invokeMethodAsync<MentionUser[]>('SearchMentionUsers', query);
                return users.slice(0, 8);
              } catch { return []; }
            },
            render: () => {
              let tippyInstance: ReturnType<typeof tippy> | null = null;
              let popup: HTMLElement | null = null;

              return {
                onStart(props: { clientRect?: (() => DOMRect | null) | null; items: MentionUser[]; command: (u: { id: string; label: string }) => void }) {
                  popup = document.createElement('div');
                  popup.className = 'wcn-mention-popup';
                  const updateList = () => {
                    popup!.innerHTML = '';
                    props.items.forEach(user => {
                      const btn = document.createElement('button');
                      btn.className = 'wcn-mention-item';
                      btn.innerHTML = `
                        ${user.photoBase64
                          ? `<img src="data:image/jpeg;base64,${user.photoBase64}" class="wcn-mention-photo">`
                          : `<span class="wcn-mention-avatar">${user.displayName[0]}</span>`
                        }
                        <span class="wcn-mention-name">${user.displayName}</span>
                        ${user.department ? `<span class="wcn-mention-dept">${user.department}</span>` : ''}
                      `;
                      btn.addEventListener('mousedown', e => {
                        e.preventDefault();
                        props.command({ id: user.id, label: user.displayName });
                      });
                      popup!.appendChild(btn);
                    });
                  };
                  updateList();

                  if (props.clientRect) {
                    tippyInstance = tippy(document.body, {
                      getReferenceClientRect: props.clientRect,
                      content:     popup,
                      trigger:     'manual',
                      interactive: true,
                      theme:       'wcn-mention',
                      placement:   'bottom-start',
                      arrow:       false,
                    });
                    (tippyInstance as any).show();
                  }
                },
                onUpdate(props: { items: MentionUser[] }) {
                  if (popup) {
                    popup.innerHTML = '';
                    props.items.forEach(user => {
                      const btn = document.createElement('button');
                      btn.className = 'wcn-mention-item';
                      btn.textContent = user.displayName;
                      popup!.appendChild(btn);
                    });
                  }
                },
                onExit() { (tippyInstance as any)?.destroy(); },
              };
            },
          },
        }),
      ],

      content: initialJson ? JSON.parse(initialJson) : '',

      onUpdate({ editor: e }) {
        // 変更を Blazor に通知（デバウンス 300ms）
        clearTimeout((window as any).__wcnUpdateTimer);
        (window as any).__wcnUpdateTimer = setTimeout(() => {
          const json = JSON.stringify(e.getJSON());
          dotnet.invokeMethodAsync('OnEditorChange', json).catch(() => {});
        }, 300);
      },
    });

    // ── ツールバーコマンドを commands.ts に委譲 ─────────────
    bindToolbarCommands(elementId, editor);
    initCommandsApi();

    _instances.set(elementId, editor);

    // バブルメニュー初期化
    createBubbleMenu(editor, container);

    // スラッシュコマンドイベントリスナー
    window.addEventListener('wcn:slash-command', () => {
      createSlashPalette(editor, editorEl);
    });

        // IssueEmbed 展開イベント
    window.addEventListener('wcn:embed-issue', (e: Event) => {
      const { start, end, data } = (e as CustomEvent).detail;
      editor.chain().focus()
        .deleteRange({ from: start, to: end })
        .insertContent({
          type:  'issueEmbed',
          attrs: {
            issueKey: data.key,
            issueId:  data.issueId,
            title:    data.title,
            status:   data.status,
            priority: data.priority,
          },
        }).run();
    });
  },

  // ── エディタ破棄 ──────────────────────────────────────────
  destroy(elementId: string) {
    const editor = _instances.get(elementId);
    if (editor) { editor.destroy(); _instances.delete(elementId); }
  },

  // ── JSON 取得（Blazor の保存時に呼び出し） ────────────────
  getJson(elementId: string): string {
    const editor = _instances.get(elementId);
    if (!editor) return '{}';
    return JSON.stringify(editor.getJSON());
  },

  // ── JSON セット（外部からコンテンツを差し替え） ───────────
  setJson(elementId: string, json: string) {
    const editor = _instances.get(elementId);
    if (!editor) return;
    editor.commands.setContent(JSON.parse(json), false);
  },

  // ── Markdown インポート（既存データの初回読み込み） ────────
  // Markdown は C# 側でプレーンテキストに変換後、
  // サーバーサイドで ProseMirror JSON に変換するのが理想だが、
  // 簡易実装として Tiptap に直接テキストとして流し込む
  setMarkdown(elementId: string, markdown: string) {
    const editor = _instances.get(elementId);
    if (!editor) return;
    // Markdown → HTML 変換は Blazor サーバー側で実施済みの HTML を受け取る
    editor.commands.setContent(markdown, false);
  },

  // ── フォーカス ────────────────────────────────────────────
  focus(elementId: string) {
    _instances.get(elementId)?.commands.focus();
  },

  // ── 画像挿入（Ctrl+V / ファイルアップロード後に呼び出し） ─
  insertImage(elementId: string, url: string, alt: string) {
    _instances.get(elementId)?.chain().focus().setImage({ src: url, alt }).run();
  },

  // ── IssueEmbed 挿入（Blazor 側から手動挿入） ──────────────
  embedIssue(elementId: string, issueKey: string, data: object) {
    const editor = _instances.get(elementId);
    if (!editor) return;
    editor.chain().focus().insertContent({
      type: 'issueEmbed',
      attrs: { issueKey, ...(data as object) },
    }).run();
  },

  // ── 読み取り専用切り替え ──────────────────────────────────
  setEditable(elementId: string, editable: boolean) {
    _instances.get(elementId)?.setEditable(editable);
  },
};
