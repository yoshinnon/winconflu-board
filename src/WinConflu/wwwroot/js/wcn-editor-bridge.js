// WinConflu.NET — wcn-editor-bridge.js
// Tiptap バンドルロード後の補完ブリッジ

(function () {
    'use strict';

    // Ctrl+V 画像貼り付け → Tiptap に直接埋め込み
    document.addEventListener('paste', async function (e) {
        const activeEditor = document.querySelector('.wcn-tiptap-content .ProseMirror:focus');
        if (!activeEditor) return;
        const items = e.clipboardData ? Array.from(e.clipboardData.items) : [];
        const imageItem = items.find(function(item) { return item.type.startsWith('image/'); });
        if (!imageItem) return;
        e.preventDefault();
        const blob = imageItem.getAsFile();
        if (!blob) return;
        const reader = new FileReader();
        reader.onloadend = function () {
            const base64 = reader.result;
            if (typeof base64 === 'string') {
                const editorContainer = activeEditor.closest('[id^="wcn-tiptap-"]');
                if (editorContainer && window.wcnEditor) {
                    window.wcnEditor.insertImage(editorContainer.id, base64, '貼り付け画像');
                }
            }
        };
        reader.readAsDataURL(blob);
    });

    if (window.location.hostname === 'localhost') {
        console.log('[WinConflu] Editor bridge loaded.');
    }
})();
