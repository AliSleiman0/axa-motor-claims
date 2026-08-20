import { describe, expect, it } from 'vitest'

/**
 * Every source file in this folder, read as text. `import.meta.glob` rather than `node:fs` because
 * `tsconfig.app.json` deliberately limits `types` to `vite/client` — this is browser code, and
 * reaching for node types here would widen that boundary for the whole app just to write a test.
 */
const sources = import.meta.glob('./*.{ts,tsx}', {
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
  const forbidden = [
    { pattern: /from\s+'\.\.\/expert\//, what: 'the expert module' },
    { pattern: /from\s+'\.\.\/pages\//, what: 'a page' },
    { pattern: /from\s+'\.\.\/admin\//, what: 'the admin module' },
    { pattern: /from\s+'\.\.\/api\/session'/, what: 'the session helpers' },
    { pattern: /from\s+'\.\.\/api\/tokens'/, what: 'the token store' },
    { pattern: /from\s+'react-router/, what: 'the router' },
  ]

  it('has source files to check', () => {
    // Without this the suite below passes vacuously on an empty match — the same trap
    // `Rule2_IsNotVacuous` guards against on the server side.
    expect(sourceFiles.length).toBeGreaterThan(4)
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
