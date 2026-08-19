import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

// Pneuma Subject Dashboard build configuration.
const dirname = import.meta.dirname;

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(dirname, './src'),
      '@components': path.resolve(dirname, './src/components'),
      '@views': path.resolve(dirname, './src/views'),
      '@context': path.resolve(dirname, './src/context'),
      '@utils': path.resolve(dirname, './src/utils'),
      '@hooks': path.resolve(dirname, './src/hooks')
    }
  },
  server: {
    port: 3011,
    host: true
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
    rollupOptions: {
      output: {
        // Vite 8 (rolldown) requires manualChunks to be a function, not an object.
        manualChunks(id) {
          if (id.includes('node_modules') &&
              /[\\/]node_modules[\\/](react-router-dom|react-router|react-dom|react|scheduler)[\\/]/.test(id)) {
            return 'vendor';
          }
          return undefined;
        }
      }
    }
  }
});
