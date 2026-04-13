// WinConflu.NET — commands.ts
// ツールバーボタン → Tiptap コマンドディスパッチャ

import type { Editor } from '@tiptap/core';

// ── window.wcnEditorCommands の初期化 ─────────────────────
export function initCommandsApi(): void {
  (window as any).wcnEditorCommands = {
    exec(elementId: string, command: string): void {
      window.dispatchEvent(new CustomEvent('wcn:toolbar-command', {
        detail: { elementId, command }
      }));
    }
  };
}

// ── エディタインスタンスにコマンドリスナーをバインド ───────
export function bindToolbarCommands(elementId: string, editor: Editor): void {
  const handler = (e: Event) => {
    const { elementId: id, command } = (e as CustomEvent).detail as {
      elementId: string; command: string;
    };
    if (id !== elementId) return;
    dispatchCommand(editor, command);
  };
  window.addEventListener('wcn:toolbar-command', handler);

  // エディタ破棄時にリスナーを解除
  editor.on('destroy', () =>
    window.removeEventListener('wcn:toolbar-command', handler));
}

function dispatchCommand(editor: Editor, command: string): void {
  const c = editor.chain().focus();
  switch (command) {
    case 'bold':          c.toggleBold().run();           break;
    case 'italic':        c.toggleItalic().run();         break;
    case 'underline':     c.toggleUnderline().run();      break;
    case 'strike':        c.toggleStrike().run();         break;
    case 'code':          c.toggleCode().run();           break;
    case 'h1':            c.toggleHeading({ level: 1 }).run(); break;
    case 'h2':            c.toggleHeading({ level: 2 }).run(); break;
    case 'h3':            c.toggleHeading({ level: 3 }).run(); break;
    case 'bulletList':    c.toggleBulletList().run();     break;
    case 'orderedList':   c.toggleOrderedList().run();    break;
    case 'taskList':      c.toggleTaskList().run();       break;
    case 'table':
      c.insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run(); break;
    case 'addRowAfter':       c.addRowAfter().run();          break;
    case 'addRowBefore':      c.addRowBefore().run();         break;
    case 'deleteRow':         c.deleteRow().run();            break;
    case 'addColumnAfter':    c.addColumnAfter().run();       break;
    case 'addColumnBefore':   c.addColumnBefore().run();      break;
    case 'deleteColumn':      c.deleteColumn().run();         break;
    case 'mergeCells':        c.mergeCells().run();           break;
    case 'splitCell':         c.splitCell().run();            break;
    case 'deleteTable':       c.deleteTable().run();          break;
    case 'toggleHeaderRow':   c.toggleHeaderRow().run();      break;
    case 'toggleHeaderColumn':c.toggleHeaderColumn().run();   break;
    case 'infoBox_info':
    case 'infoBox_warning':
    case 'infoBox_tip':
    case 'infoBox_danger': {
      const variant = command.split('_')[1];
      c.insertContent({
        type: 'infoBox', attrs: { variant },
        content: [{ type: 'paragraph' }],
      }).run();
      break;
    }
    case 'link': {
      const url = prompt('リンク URL を入力してください:');
      if (url) c.setLink({ href: url, target: '_blank' }).run();
      break;
    }
    case 'codeBlock':     c.toggleCodeBlock().run();      break;
    case 'hr':            c.setHorizontalRule().run();    break;
    case 'blockquote':    c.toggleBlockquote().run();     break;
    case 'clearFormat':   c.clearNodes().unsetAllMarks().run(); break;
    case 'undo':          c.undo().run();                  break;
    case 'redo':          c.redo().run();                  break;
    case 'alignLeft':     c.setTextAlign('left').run();    break;
    case 'alignCenter':   c.setTextAlign('center').run();  break;
    case 'alignRight':    c.setTextAlign('right').run();   break;
    default:
      console.warn(`[wcnEditor] unknown command: "${command}"`);
  }
}
