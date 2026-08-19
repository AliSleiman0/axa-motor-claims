import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Dev-only proxy to the local API (launchSettings.json http profile).
    proxy: {
      '/api': 'http://localhost:5180',
      '/auth': 'http://localhost:5180',
    },
  },
})
