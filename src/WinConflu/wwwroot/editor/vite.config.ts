import { defineConfig } from 'vite';
import { resolve } from 'path';

export default defineConfig({
  build: {
    lib: {
      entry:    resolve(__dirname, 'src/editor.ts'),
      name:     'WcnEditor',
      fileName: 'wcn-editor',
      formats:  ['iife'],        // ブラウザ直接読み込み用の即時実行形式
    },
    outDir:          '../dist',  // wwwroot/dist/ に出力
    emptyOutDir:     true,
    minify:          true,
    sourcemap:       true,
    rollupOptions: {
      output: {
        // グローバル変数名
        name: 'WcnEditor',
      }
    }
  },
});
