import { defineConfig } from 'vitest/config'
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
  test: {
    // Slice 2.4 stands this up because the geolocation-denied path and the double-press guard are
    // browser facts that `dotnet test` cannot reach. Slice 2.5's clarity gate needs it outright.
    environment: 'jsdom',
    include: ['src/**/*.test.{ts,tsx}'],
    setupFiles: ['src/testing/setup.ts'],
    // No `globals`: every test imports describe/it/expect explicitly, so tsconfig's `types` and the
    // ESLint globals list stay as they are.
    restoreMocks: true,
    unstubGlobals: true,
  },
})
