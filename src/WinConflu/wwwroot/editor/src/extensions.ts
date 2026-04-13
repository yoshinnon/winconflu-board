// WinConflu.NET — extensions.ts
// WinConflu 固有の Tiptap カスタム拡張

import { Node, mergeAttributes, Extension } from '@tiptap/core';
import { Plugin, PluginKey } from '@tiptap/pm/state';
import type { DotNetHelper, MentionUser, IssueBadgeData } from './types';

// ────────────────────────────────────────────────────────────
// InfoBox Node — Confluence の「パネルマクロ」相当
// ::::info / ::::warning / ::::tip / ::::danger
// ────────────────────────────────────────────────────────────

export const InfoBox = Node.create({
  name: 'infoBox',
  group: 'block',
  content: 'block+',
  draggable: true,

  addAttributes() {
    return {
      variant: {
        default: 'info',
        parseHTML: el => el.getAttribute('data-variant') ?? 'info',
      },
    };
  },

  parseHTML() {
    return [{ tag: 'div[data-type="info-box"]' }];
  },

  renderHTML({ node, HTMLAttributes }) {
    const variant = node.attrs.variant as string;
    const icons: Record<string, string> = {
      info:    'ℹ️',
      warning: '⚠️',
      tip:     '💡',
      danger:  '🚨',
    };
    return [
      'div',
      mergeAttributes(HTMLAttributes, {
        'data-type':    'info-box',
        'data-variant': variant,
        class:          `wcn-infobox wcn-infobox--${variant}`,
      }),
      ['div', { class: 'wcn-infobox__icon' }, icons[variant] ?? 'ℹ️'],
      ['div', { class: 'wcn-infobox__body' }, 0],
    ];
  },

  addNodeView() {
    return ({ node, HTMLAttributes, getPos, editor }) => {
      const variant  = node.attrs.variant as string;
      const icons: Record<string, string> = {
        info: 'ℹ️', warning: '⚠️', tip: '💡', danger: '🚨'
      };
      const colors: Record<string, string> = {
        info: '#E6F1FB', warning: '#FAEEDA', tip: '#EAF3DE', danger: '#FCEBEB'
      };
      const borders: Record<string, string> = {
        info: '#185FA5', warning: '#BA7517', tip: '#3B6D11', danger: '#A32D2D'
      };

      const outer = document.createElement('div');
      outer.setAttribute('data-type', 'info-box');
      outer.setAttribute('data-variant', variant);
      outer.style.cssText = `
        display:flex;gap:12px;padding:12px 16px;border-radius:8px;margin:1rem 0;
        background:${colors[variant] ?? '#E6F1FB'};
        border-left:4px solid ${borders[variant] ?? '#185FA5'};
      `;

      const iconEl = document.createElement('div');
      iconEl.textContent = icons[variant] ?? 'ℹ️';
      iconEl.style.cssText = 'font-size:18px;flex-shrink:0;margin-top:2px;';

      const body = document.createElement('div');
      body.style.cssText = 'flex:1;min-width:0;';

      outer.appendChild(iconEl);
      outer.appendChild(body);

      return { dom: outer, contentDOM: body };
    };
  },
});

// ────────────────────────────────────────────────────────────
// IssueEmbed Node — Boards チケットの埋め込みカード
// [[WCN-12]] と入力すると自動展開
// ────────────────────────────────────────────────────────────

export const IssueEmbed = Node.create({
  name: 'issueEmbed',
  group: 'inline',
  inline: true,
  atom: true,   // 内部は編集不可（単一原子ノード）

  addAttributes() {
    return {
      issueKey:  { default: '' },
      issueId:   { default: null },
      title:     { default: '読み込み中...' },
      status:    { default: '' },
      priority:  { default: '' },
    };
  },

  parseHTML() {
    return [{ tag: 'span[data-type="issue-embed"]' }];
  },

  renderHTML({ node, HTMLAttributes }) {
    const statusColors: Record<string, string> = {
      Todo: '#888780', Doing: '#BA7517', Done: '#3B6D11', Verified: '#185FA5',
    };
    const color = statusColors[node.attrs.status] ?? '#888780';
    return [
      'span',
      mergeAttributes(HTMLAttributes, {
        'data-type': 'issue-embed',
        'data-key':  node.attrs.issueKey,
        class:       'wcn-issue-embed',
        contenteditable: 'false',
      }),
      ['span', { class: 'wcn-issue-embed__key', style: `color:${color}` }, node.attrs.issueKey],
      ['span', { class: 'wcn-issue-embed__title' }, ` ${node.attrs.title}`],
      ['span', {
        class: 'wcn-issue-embed__status',
        style: `background:${color}20;color:${color}`,
      }, node.attrs.status],
    ];
  },

  addNodeView() {
    return ({ node }) => {
      const statusColors: Record<string, string> = {
        Todo: '#888780', Doing: '#BA7517', Done: '#3B6D11', Verified: '#185FA5',
      };
      const color  = statusColors[node.attrs.status] ?? '#888780';
      const span   = document.createElement('span');
      span.setAttribute('data-type', 'issue-embed');
      span.setAttribute('contenteditable', 'false');
      span.style.cssText = `
        display:inline-flex;align-items:center;gap:6px;padding:2px 8px;
        border:1px solid ${color}40;border-radius:4px;font-size:13px;
        background:${color}10;cursor:pointer;user-select:none;
      `;
      span.innerHTML = `
        <span style="font-family:monospace;font-weight:500;color:${color}">${node.attrs.issueKey}</span>
        <span style="max-width:200px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${node.attrs.title}</span>
        <span style="font-size:11px;background:${color}20;color:${color};padding:1px 5px;border-radius:3px">${node.attrs.status}</span>
      `;
      return { dom: span, ignoreMutation: () => true };
    };
  },
});

// ────────────────────────────────────────────────────────────
// SlashCommand Extension — / 入力でコマンドパレット表示
// ────────────────────────────────────────────────────────────

export function createSlashCommandExtension(dotnet: DotNetHelper) {
  return Extension.create({
    name: 'slashCommand',

    addProseMirrorPlugins() {
      return [
        new Plugin({
          key: new PluginKey('slashCommand'),
          props: {
            handleKeyDown(view, event) {
              // '/' キーを検知してパレットを開くシグナルを送る
              // 実際のポップアップは wcn-editor.ts 側の UI ロジックで処理
              if (event.key === '/' ) {
                // カーソル位置の直前がスペースまたは行頭のみ発火
                const { from } = view.state.selection;
                const textBefore = view.state.doc.textBetween(
                  Math.max(0, from - 1), from);
                if (textBefore === '' || textBefore === ' ' || textBefore === '\n') {
                  // イベントを消費せず、wcn.editor.onSlashCommand を呼ぶ
                  setTimeout(() => {
                    window.dispatchEvent(new CustomEvent('wcn:slash-command', {
                      detail: { from }
                    }));
                  }, 0);
                }
              }
              return false;
            },
          },
        }),
      ];
    },
  });
}

// ────────────────────────────────────────────────────────────
// IssuePattern Extension — [[KEY-NNN]] 自動展開
// ────────────────────────────────────────────────────────────

export function createIssuePatternExtension(dotnet: DotNetHelper) {
  return Extension.create({
    name: 'issuePattern',

    addProseMirrorPlugins() {
      return [
        new Plugin({
          key: new PluginKey('issuePattern'),
          appendTransaction(transactions, _oldState, newState) {
            // テキスト変更があった場合のみ処理
            if (!transactions.some(tr => tr.docChanged)) return null;

            const pattern  = /\[\[([A-Z]+-\d+)\]\]/g;
            const tr       = newState.tr;
            let changed    = false;

            newState.doc.descendants((node, pos) => {
              if (node.type.name !== 'text' || !node.text) return;
              let match: RegExpExecArray | null;
              while ((match = pattern.exec(node.text)) !== null) {
                const start   = pos + match.index;
                const end     = start + match[0].length;
                const issueKey = match[1];

                // 非同期で Blazor に問い合わせ、展開
                dotnet.invokeMethodAsync<IssueBadgeData | null>('GetIssueBadge', issueKey)
                  .then(data => {
                    if (!data) return;
                    // エディタに IssueEmbed ノードを挿入（次の更新サイクルで）
                    window.dispatchEvent(new CustomEvent('wcn:embed-issue', {
                      detail: { start, end, data }
                    }));
                  })
                  .catch(() => {/* 存在しないキーは無視 */});
              }
            });

            return changed ? tr : null;
          },
        }),
      ];
    },
  });
}
