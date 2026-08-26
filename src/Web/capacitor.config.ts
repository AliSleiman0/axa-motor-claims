import type { CapacitorConfig } from '@capacitor/cli'

/**
 * The Android shell's build configuration (slice 6.3; `capacitor.config.json` until then).
 *
 * **TypeScript rather than JSON so the release shape is the default and the dev shape is the
 * exception.** The JSON version carried `server.url`, `cleartext: true` and
 * `webContentsDebuggingEnabled: true` unconditionally, because the spike needed all three to point a
 * handset at a laptop. Every one of them is a hole in a shipped app: `server.url` makes the APK load
 * its web layer from a machine that will not be there, `cleartext` re-permits plain http for the
 * whole application, and `webContentsDebuggingEnabled` lets anyone with `adb` attach a DevTools
 * session to a signed build and read an expert's claims.
 *
 * So they exist **only when `CAP_SERVER_URL` is set**, which no release build sets and
 * `scripts/android-release.ps1` asserts is unset before it will run. Unset is the clean shape:
 * `webDir: 'dist'`, nothing else. A flag that has to be remembered before every release is a flag
 * that is eventually forgotten; this one has to be remembered in order to *develop*, which is the
 * direction that fails safe.
 *
 * The `appId` is `com.axa.motorclaims` rather than a placeholder, and that is a deliberate exception
 * to CLAUDE.md's rule that client literals live only in `appsettings.Placeholders.json`. An Android
 * applicationId is not a config value: it is compiled into the APK, it is what the Firebase app is
 * registered against, and it is the identity every installed handset knows — so it cannot be swapped
 * at deployment the way an Appendix A value can. Recorded, with its cost, in `scope-decisions.md`.
 */
const devServer = process.env.CAP_SERVER_URL

const config: CapacitorConfig = {
  appId: 'com.axa.motorclaims',
  appName: 'AXA Motor Claims',
  webDir: 'dist',

  ...(devServer
    ? {
        server: {
          url: devServer,
          // Only ever alongside a dev server. The Samsung reaches the laptop through
          // `adb reverse tcp:5173 tcp:5173`, and Chromium treats `http://localhost` as a
          // potentially-trustworthy origin, so the WebView gets a secure context with no
          // certificate — which is why this is http at all (see docs/device-spike-2026-08-22.md).
          cleartext: true,
        },
        android: {
          webContentsDebuggingEnabled: true,
        },
      }
    : {}),
}

export default config
