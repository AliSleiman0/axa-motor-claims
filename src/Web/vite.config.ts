import { readFileSync } from 'node:fs'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

/*
 * Slice 6.3a (device spike). Safari will not give a page camera, geolocation, service workers or web
 * push without a secure context, and it will not trust the ASP.NET dev certificate — so the iPhone
 * needs this server over HTTPS on the LAN, with an mkcert leaf the phone's profile trusts.
 *
 * Behind env vars on purpose: unset, `server` is byte-for-byte what it was, so `npm run dev` and
 * `scripts/demo-reset.ps1` are untouched and the week-4 demo cannot be broken by a spike. Set, this
 * is a second Vite on another port beside the first. `scripts/spike-device.ps1` is what sets them.
 *
 * The cert lives under `demo-artifacts/` (already gitignored) — no key or CA ever enters the repo.
 */
const httpsCert = process.env.SPIKE_HTTPS_CERT
const httpsKey = process.env.SPIKE_HTTPS_KEY
const https =
  httpsCert && httpsKey ? { cert: readFileSync(httpsCert), key: readFileSync(httpsKey) } : undefined

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Only when serving a phone: `host` binds the LAN interface, which the default (localhost) does not.
    ...(https ? { host: true, https } : {}),
    // Dev-only proxy to the local API (launchSettings.json http profile). The phones reach the API
    // through here, which is why the API itself never needs a certificate or a LAN binding.
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
