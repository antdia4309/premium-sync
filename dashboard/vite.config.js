import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// The dashboard talks to all three services through this dev server, so the browser
// only ever sees one origin (no CORS setup needed on the APIs).
const strip = (prefix) => (path) => path.replace(new RegExp(`^${prefix}`), '');

export default defineConfig({
  plugins: [react()],
  server: {
    host: true,
    port: 5173,
    allowedHosts: ['.app.github.dev', 'localhost'], // GitHub Codespaces forwarded URLs
    proxy: {
      '/policy': { target: 'http://localhost:5001', rewrite: strip('/policy') },
      '/billing': { target: 'http://localhost:5002', rewrite: strip('/billing') },
      '/recon': { target: 'http://localhost:5003', rewrite: strip('/recon') },
    },
  },
});
