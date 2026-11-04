import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath } from 'node:url'

// Dev server: the app on :5173, API and live hub proxied to the .NET backend on :5080.
export default defineConfig({
  plugins: [react()],
  resolve: { alias: { '@gridline/calc': fileURLToPath(new URL('./packages/calc/src/index.ts', import.meta.url)) } },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: true },
      '/hubs': { target: 'http://localhost:5080', ws: true, changeOrigin: true },
    },
  },
  build: { outDir: 'dist', sourcemap: true, chunkSizeWarningLimit: 3000 },
  test: { include: ['packages/**/*.test.ts', 'src/**/*.test.ts', 'src/**/*.test.tsx'] },
} as any)
