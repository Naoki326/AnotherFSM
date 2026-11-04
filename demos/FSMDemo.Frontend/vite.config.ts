import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  base: process.env.VITE_BASE_PATH || '/',
  build: process.env.RUNTIME_CONTRACT === 'true'
    ? { rolldownOptions: { input: { main: 'index.html', contract: 'test/runtime.html' } } }
    : undefined,
  server: {
    host: '127.0.0.1',
    port: 5174,
  },
})
