import { describe, expect, it } from 'vitest'

/**
 * Every source file in this folder **and below it**, read as text. `import.meta.glob` rather than
 * `node:fs` because `tsconfig.app.json` deliberately limits `types` to `vite/client` — this is
 * browser code, and reaching for node types here would widen that boundary for the whole app just
 * to write a test.
 *
 * The `**` is load-bearing (widened in slice 3.1, when `diagram/` arrived): a top-level-only glob
 * lets a subfolder import whatever it likes with the guard still green, which is the exact failure
 * mode this file exists to prevent — a rule that quietly stops covering the code it was written for.
 */
const sources = import.meta.glob('./**/*.{ts,tsx}', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

const sourceFiles = Object.entries(sources).filter(([name]) => !name.includes('.test.'))

/** The shared component layer, added in slice 4.4 — see the second `describe` below. */
const uiSources = import.meta.glob('../ui/**/*.{ts,tsx}', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

const uiFiles = Object.entries(uiSources).filter(([name]) => !name.includes('.test.'))

/**
 * The web-side counterpart to the NetArchTest rules in `src/Api.Tests/Architecture`.
 *
 * design.md §11 holds the calendar at 8 weeks partly by reusing this capture/clarity pipeline for
 * the garage flow (5.1) and the Broker Option 2 public page (5.3/6.1). That reuse is not a comment,
 * it is a property of the code: the moment anything here imports an expert hook, an expert query key
 * or an expert route, the public page cannot use it and the saving evaporates — and nothing else in
 * the suite would notice, because every expert test would still pass.
 *
 * The public page is unauthenticated (§3's hard boundary), so a stray import of the token store or
 * the session helpers would be worse than untidy.
 */
describe('the media module stays reusable', () => {
  // `(\.\.\/)+` rather than a single `../`: a file in `diagram/` reaches the expert module as
  // `../../expert/`, which a depth-fixed pattern does not match at all. Made depth-agnostic in
  // slice 3.1 alongside the glob above, and verified by planting an import inside the subfolder.
  const forbidden = [
    { pattern: /from\s+'(\.\.\/)+expert\//, what: 'the expert module' },
    // Added in slice 4.2 with the modules themselves. The argument is the expert one verbatim: the
    // Option 2 public page (5.3/6.1) cannot import a garage hook or an officer query key, so neither
    // may this module — and the cheapest moment to say so is the moment those folders start existing.
    { pattern: /from\s+'(\.\.\/)+garage\//, what: 'the garage module' },
    { pattern: /from\s+'(\.\.\/)+officer\//, what: 'the officer module' },
    { pattern: /from\s+'(\.\.\/)+broker\//, what: 'the broker module' },
    { pattern: /from\s+'(\.\.\/)+pages\//, what: 'a page' },
    { pattern: /from\s+'(\.\.\/)+admin\//, what: 'the admin module' },
    { pattern: /from\s+'(\.\.\/)+api\/session'/, what: 'the session helpers' },
    { pattern: /from\s+'(\.\.\/)+api\/tokens'/, what: 'the token store' },
    { pattern: /from\s+'react-router/, what: 'the router' },
  ]

  it('has source files to check', () => {
    // Without this the suite below passes vacuously on an empty match — the same trap
    // `Rule2_IsNotVacuous` guards against on the server side. Raised in slice 3.1, and again in 4.2
    // when `png.ts` and `approval/` arrived: the count is the only thing that would notice the glob
    // silently ceasing to match a folder.
    expect(sourceFiles.length).toBeGreaterThan(17)
  })

  it('reaches into the subfolders', () => {
    // The count above cannot tell "13 files at the top level" from "9 plus 4 nested", and the whole
    // point of widening the glob was the nested ones. Both subfolders are named, because a guard
    // that covers one of them is exactly the half-covering rule this file exists to prevent.
    expect(sourceFiles.some(([name]) => name.includes('/diagram/'))).toBe(true)
    expect(sourceFiles.some(([name]) => name.includes('/approval/'))).toBe(true)
  })

  it.each(sourceFiles)('%s imports nothing caller-specific', (name, source) => {
    for (const { pattern, what } of forbidden) {
      expect(
        pattern.test(source),
        `${name} imports ${what}; the garage and public-page flows must reuse this module unchanged`,
      ).toBe(false)
    }
  })

  /**
   * Slice 4.4 gave this module something new to import: `ui/`. Most of that folder is presentational
   * and safe, but **`AppShell`, `AppHeader` and `useSignOut` are not** — they read the session, the
   * token store and the router, which is exactly what the rules above exist to keep out. An
   * allow-list rather than a ban, because the ban would have to be rewritten every time `ui/` grows a
   * component, and the version that is never rewritten is the one that quietly stops covering
   * anything (slice 3.1's lesson, in a new place).
   */
  const ALLOWED_UI = ['Button', 'Banner', 'StatusChip', 'DetailTable', 'DocumentRow', 'tones']

  it.each(sourceFiles)('%s imports only presentational parts of ui/', (name, source) => {
    for (const match of source.matchAll(/from\s+'(?:\.\.\/)+ui\/([A-Za-z]+)'/g)) {
      expect(
        ALLOWED_UI.includes(match[1]),
        `${name} imports ui/${match[1]}; only ${ALLOWED_UI.join(', ')} are safe here — the shell ` +
          'components read the session and the token store, which the public page has neither of',
      ).toBe(true)
    }
  })
})

/**
 * The same argument one folder over.
 *
 * `ui/` is the chrome every role shares, so a component that reached into `garage/` or `officer/`
 * would make the header un-renderable for the other four — and, through the allow-list above, could
 * couple `media/` to a role module transitively, with the guard beside it still reading green.
 *
 * The router is **not** forbidden here, unlike in `media/`: `DesktopNav` navigates and `useSignOut`
 * redirects, and that is the job. The rule that matters is that no shared component knows about one
 * role's data.
 */
describe('the ui layer stays role-agnostic', () => {
  const forbidden = [
    { pattern: /from\s+'(\.\.\/)+expert\//, what: 'the expert module' },
    { pattern: /from\s+'(\.\.\/)+garage\//, what: 'the garage module' },
    { pattern: /from\s+'(\.\.\/)+officer\//, what: 'the officer module' },
    { pattern: /from\s+'(\.\.\/)+broker\//, what: 'the broker module' },
    { pattern: /from\s+'(\.\.\/)+admin\//, what: 'the admin module' },
    { pattern: /from\s+'(\.\.\/)+pages\//, what: 'a page' },
  ]

  it('has source files to check', () => {
    // Non-vacuity, the same guard `Rule2_IsNotVacuous` gives the server rules: without it this whole
    // block passes on an empty match the day the folder is renamed.
    expect(uiFiles.length).toBeGreaterThan(9)
  })

  it.each(uiFiles)('%s imports no role module', (name, source) => {
    for (const { pattern, what } of forbidden) {
      expect(
        pattern.test(source),
        `${name} imports ${what}; every role shares these components`,
      ).toBe(false)
    }
  })
})
