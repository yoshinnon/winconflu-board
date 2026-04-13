// ============================================================
// WinConflu.NET — wcn.js
// Blazor JS インタロップ
// ============================================================

window.wcn = {

    getTextSelection: function (containerId) {
        const sel = window.getSelection();
        if (!sel || sel.isCollapsed || sel.toString().trim().length < 2) return null;
        const container = document.getElementById(containerId);
        if (!container) return null;
        const range = sel.getRangeAt(0);
        const preRange = document.createRange();
        preRange.selectNodeContents(container);
        preRange.setEnd(range.startContainer, range.startOffset);
        const start = preRange.toString().length;
        const text  = sel.toString();
        const end   = start + text.length;
        const rect          = range.getBoundingClientRect();
        const containerRect = container.getBoundingClientRect();
        return {
            text:         text,
            start:        start,
            end:          end,
            boundingTop:  rect.top  - containerRect.top  + container.scrollTop,
            boundingLeft: Math.min(rect.left - containerRect.left, containerRect.width - 200)
        };
    },

    getClipboardImage: function () {
        return new Promise((resolve) => {
            if (!navigator.clipboard || !navigator.clipboard.read) { resolve(null); return; }
            navigator.clipboard.read().then(items => {
                for (const item of items) {
                    for (const type of item.types) {
                        if (type.startsWith('image/')) {
                            item.getType(type).then(blob => {
                                const reader = new FileReader();
                                reader.onloadend = () => resolve(reader.result);
                                reader.readAsDataURL(blob);
                            });
                            return;
                        }
                    }
                }
                resolve(null);
            }).catch(() => resolve(null));
        });
    },

    applyAnnotationHighlights: function (containerId, annotations) {
        const container = document.getElementById(containerId);
        if (!container) return;
        container.querySelectorAll('.wcn-annotation-highlight').forEach(el => {
            const parent = el.parentNode;
            while (el.firstChild) parent.insertBefore(el.firstChild, el);
            parent.removeChild(el);
        });
        if (!annotations || annotations.length === 0) return;
        const sorted = [...annotations].sort((a, b) => b.startOffset - a.startOffset);
        sorted.forEach(ann => wcn._highlightTextRange(container, ann.startOffset, ann.endOffset, ann.id, ann.status));
    },

    _highlightTextRange: function (container, start, end, annotationId, status) {
        let charCount = 0;
        const walker  = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
        let node;
        while ((node = walker.nextNode())) {
            const nodeEnd = charCount + node.length;
            if (charCount <= start && nodeEnd >= end) {
                try {
                    const range = document.createRange();
                    range.setStart(node, start - charCount);
                    range.setEnd(node, end - charCount);
                    const span = document.createElement('mark');
                    span.className = 'wcn-annotation-highlight ' + (status === 'Resolved' ? 'resolved' : 'open');
                    span.dataset.annId = annotationId;
                    span.title = 'コメントあり';
                    range.surroundContents(span);
                } catch (e) {}
                break;
            }
            charCount = nodeEnd;
        }
    },

    initCodeCopyButtons: function () {
        document.querySelectorAll('pre.wcn-code').forEach(pre => {
            if (pre.querySelector('.wcn-copy-btn')) return;
            const btn = document.createElement('button');
            btn.className = 'wcn-copy-btn';
            btn.textContent = 'コピー';
            btn.onclick = () => {
                const code = pre.querySelector('code') ? pre.querySelector('code').textContent : '';
                navigator.clipboard.writeText(code).then(() => {
                    btn.textContent = 'コピー済!';
                    setTimeout(() => { btn.textContent = 'コピー'; }, 1500);
                });
            };
            pre.style.position = 'relative';
            pre.appendChild(btn);
        });
    },

    init: function () {
        wcn.initCodeCopyButtons();
        const observer = new MutationObserver(() => wcn.initCodeCopyButtons());
        observer.observe(document.body, { childList: true, subtree: true });
    }
};

// ── Tiptap ツールバーコマンドブリッジ ────────────────────────
// TiptapEditorComponent.razor の ExecCmd('command') から呼ばれる
// editor.ts 側で 'wcn:toolbar-command' イベントを受け取ってコマンド実行する
window.wcnEditorCommands = {
    exec: function (editorId, command) {
        window.dispatchEvent(new CustomEvent('wcn:toolbar-command', {
            detail: { editorId: editorId, command: command }
        }));
    }
};

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', wcn.init);
} else {
    wcn.init();
}
