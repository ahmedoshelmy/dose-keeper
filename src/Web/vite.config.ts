import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Dev proxy mirrors nginx.conf: the Functions host (func start or compose) serves /api.
export default defineConfig({
  plugins: [react()],
  server: { proxy: { '/api': 'http://localhost:7071' } },
  test: { environment: 'node' },
})
