import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    // The API has no CORS policy, and a reviewer token should not be handed to a second origin. Proxying
    // /api means the browser only ever talks to the dev server, which relays the call server to server.
    proxy: { '/api': { target: 'http://localhost:5000', changeOrigin: true, rewrite: (path) => path.replace(/^\/api/, '') } },
  },
});
