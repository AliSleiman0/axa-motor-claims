import js from '@eslint/js'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import { globalIgnores } from 'eslint/config'

export default tseslint.config([
  /*
   * `android` joins `dist` here in slice 6.3a. Everything under it is either Java/Gradle or a *copy*
   * — Capacitor's own `native-bridge.js`, and `dist` itself, which Gradle mirrors into
   * `app/src/main/assets` and again into `app/build/intermediates`. Linting a copy reported the same
   * file three times and failed the build on a rule its own inline disable comments name.
   *
   * This is not a guard quietly narrowing: authored web code cannot appear under `android/` by
   * construction, because `capacitor.config.ts` sets `webDir: 'dist'` and the native project only
   * ever receives what the Vite build emits. `src/` — where `reusability.test.ts` and every other
   * guard operate — is untouched. (It was `capacitor.config.json` until slice 6.3 moved the dev-only
   * `server.url` behind an env var; the argument is unchanged.)
   */
  globalIgnores(['dist', 'android']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      tseslint.configs.recommended,
      reactHooks.configs.flat['recommended-latest'],
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      ecmaVersion: 2023,
      globals: globals.browser,
    },
  },
  {
    // The service worker (slice 3.4). Plain JS in `public/`, so TypeScript never sees it —
    // `tsconfig.app.json` includes only `src`. Linting it is the only automated check it gets, and
    // without the serviceworker globals every `self`, `clients` and `registration` in it reads as an
    // undefined variable. `/* eslint-env */` comments are gone in flat config, hence a block here.
    files: ['public/sw.js'],
    extends: [js.configs.recommended],
    languageOptions: {
      ecmaVersion: 2023,
      sourceType: 'script',
      globals: globals.serviceworker,
    },
  },
])
