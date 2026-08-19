import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

const dirname = import.meta.dirname;

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(dirname, './src'),
      '@components': path.resolve(dirname, './src/components'),
      '@views': path.resolve(dirname, './src/views'),
      '@context': path.resolve(dirname, './src/context'),
      '@utils': path.resolve(dirname, './src/utils')
    }
  },
  server: {
    port: 3012,
    host: true
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
    rollupOptions: {
      output: {
        // Vite 8 (rolldown) requires manualChunks to be a function, not an object.
        manualChunks(id) {
          if (!id.includes('node_modules')) return undefined;
          if (/[\\/]node_modules[\\/](react-router-dom|react-router|react-dom|react|scheduler)[\\/]/.test(id)) {
            return 'vendor';
          }
          if (/[\\/]node_modules[\\/](i18next-browser-languagedetector|react-i18next|i18next|html-parse-stringify|void-elements)[\\/]/.test(id)) {
            return 'i18n';
          }
          return undefined;
        }
      }
    }
  }
});
