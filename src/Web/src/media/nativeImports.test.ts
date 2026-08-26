import { describe, expect, it } from 'vitest'

/**
 * Capacitor packages may only be reached through a **dynamic** `import()` (slice 6.3).
 *
 * **Two things depend on this and neither is visible in a normal test run.**
 *
 * The first is that jsdom must never load a Capacitor module. `@capacitor/camera` and
 * `@capacitor/push-notifications` reach for a native bridge at module scope; a top-level
 * `import … from '@capacitor/…'` anywhere in a file the web suite touches drags that in, and what
 * comes back is either a crash or — worse — a stub that resolves, so the tests would pass while
 * proving something about a shim rather than about the app.
 *
 * The second is the browser build. Rollup keeps a dynamically imported module in its own async
 * chunk, fetched only when the expression runs; a static import hoists it into the main bundle, so
 * every officer, broker and admin on a desktop would download the native camera plugin on first
 * paint and it would execute at load. The whole seam is built on that not happening.
 *
 * Written as a source scan rather than an import graph because it is the *syntax* that decides:
 * `import('@capacitor/camera')` and `import x from '@capacitor/camera'` resolve to the same module
 * and differ only in when.
 */
const sources = import.meta.glob('../**/*.{ts,tsx}', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

const sourceFiles = Object.entries(sources).filter(([name]) => !name.includes('.test.'))

/** `import … from '@capacitor/x'`, `export … from '@capacitor/x'`, and bare `import '@capacitor/x'`. */
const STATIC_CAPACITOR =
  /(?:^|\n)\s*(?:import|export)\s(?:[^'"\n]*\sfrom\s)?['"]@capacitor\/[^'"]+['"]/

/** `import('@capacitor/x')` — the permitted form. */
const DYNAMIC_CAPACITOR = /\bimport\(\s*['"]@capacitor\/[^'"]+['"]\s*\)/

describe('Capacitor modules stay behind dynamic imports', () => {
  it('has source files to check', () => {
    // Non-vacuity: the glob reaches out of `media/` into the whole of `src/`, so a broken pattern
    // that matched nothing would otherwise leave this suite silently green.
    expect(sourceFiles.length).toBeGreaterThan(80)
  })

  it.each(sourceFiles)('%s imports no Capacitor package statically', (name, source) => {
    expect(
      STATIC_CAPACITOR.test(source),
      `${name} imports a @capacitor package at the top level. It must be a dynamic import(): a ` +
        'static one loads the native bridge into jsdom, and hoists the plugin into the main ' +
        'browser bundle where it executes on first paint for every desktop role',
    ).toBe(false)
  })

  it('still reaches the files that use Capacitor at all', () => {
    // The counterpart to the ban, and the reason it is not vacuous: if the seams were deleted or
    // renamed, the rule above would pass over a codebase with no Capacitor in it and say nothing.
    const dynamic = sourceFiles.filter(([, source]) => DYNAMIC_CAPACITOR.test(source))

    expect(dynamic.map(([name]) => name).sort()).toEqual([
      '../push/nativeTaps.ts',
      '../push/nativeToken.ts',
      './nativeShell.ts',
    ])
  })

  it('detects a static import when there is one', () => {
    // The guard proved against a planted violation rather than trusted — CLAUDE.md's "pair every
    // guard with a non-vacuity assertion", answered here by exercising the pattern itself, since
    // planting a real one would mean editing a shipped file.
    expect(STATIC_CAPACITOR.test("import { Camera } from '@capacitor/camera'\n")).toBe(true)
    expect(STATIC_CAPACITOR.test("import '@capacitor/camera'\n")).toBe(true)
    expect(STATIC_CAPACITOR.test("export { Camera } from '@capacitor/camera'\n")).toBe(true)
    expect(STATIC_CAPACITOR.test("const x = await import('@capacitor/camera')\n")).toBe(false)
  })
})
