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
    { pattern: /from\s+'(\.\.\/)+pages\//, what: 'a page' },
    { pattern: /from\s+'(\.\.\/)+admin\//, what: 'the admin module' },
    { pattern: /from\s+'(\.\.\/)+api\/session'/, what: 'the session helpers' },
    { pattern: /from\s+'(\.\.\/)+api\/tokens'/, what: 'the token store' },
    { pattern: /from\s+'react-router/, what: 'the router' },
  ]

  it('has source files to check', () => {
    // Without this the suite below passes vacuously on an empty match — the same trap
    // `Rule2_IsNotVacuous` guards against on the server side. Raised in slice 3.1: the count is the
    // only thing that would notice the glob silently ceasing to match a folder.
    expect(sourceFiles.length).toBeGreaterThan(14)
  })

  it('reaches into the subfolders', () => {
    // The count above cannot tell "13 files at the top level" from "9 plus 4 nested", and the whole
    // point of widening the glob was the nested ones.
    expect(sourceFiles.some(([name]) => name.includes('/diagram/'))).toBe(true)
  })

  it.each(sourceFiles)('%s imports nothing caller-specific', (name, source) => {
    for (const { pattern, what } of forbidden) {
      expect(
        pattern.test(source),
        `${name} imports ${what}; the garage and public-page flows must reuse this module unchanged`,
      ).toBe(false)
    }
  })
})
