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

/** The public page, added in slice 5.3 — see the third `describe` below. */
const publicSources = import.meta.glob('../public/**/*.{ts,tsx}', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>

const publicFiles = Object.entries(publicSources).filter(([name]) => !name.includes('.test.'))

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
    // point of widening the glob was the nested ones. **Every** subfolder is named, because a guard
    // that covers some of them is exactly the half-covering rule this file exists to prevent —
    // `carshots/` was added in slice 6.1 and named here in the same commit.
    expect(sourceFiles.some(([name]) => name.includes('/diagram/'))).toBe(true)
    expect(sourceFiles.some(([name]) => name.includes('/approval/'))).toBe(true)
    expect(sourceFiles.some(([name]) => name.includes('/carshots/'))).toBe(true)
  })

  /**
   * **No §7.1 bucket name anywhere in this module** (slice 6.1).
   *
   * The import bans above stop `media/` reaching into a caller. This stops the subtler version: a
   * component that *knows* which bucket it is for. `CarSideSelector` draws a car and reports which
   * side was tapped; the moment it also knew that the roof goes to `public_car_roof`, it would be an
   * Option 2 component sitting in the shared folder, and the garage flow that wants the same picker
   * next year would copy it instead of using it.
   *
   * Every bucket is listed, not just the five new ones — the whole point is that none of them belongs
   * here, and a list that grew only when somebody remembered would be the guard rotting quietly. The
   * match is on the **full** bucket name, so `regions.ts`'s panel ids (`roof`, `bonnet`, …) and
   * `carshots/sides.ts`'s zone ids (`front`, `rear`, …) are untouched: those are parts of a drawing,
   * which is exactly what this module is allowed to know about.
   */
  const BUCKET_NAMES = [
    'insured_documents',
    'insured_car_photo',
    'tp_documents',
    'tp_car_photo',
    'expert_report',
    'voice_note',
    'damage_diagram',
    'garage_documents',
    'garage_car_photo',
    'approval_image',
    'repair_photo',
    'discharge',
    'invoice',
    'broker_document',
    'public_document',
    'public_car_front',
    'public_car_rear',
    'public_car_left',
    'public_car_right',
    'public_car_roof',
  ]

  it('has every bucket to check', () => {
    // Non-vacuity again, from the other direction: the list has to match the server's, or the rule
    // passes because it is looking for names nothing uses. Twenty is `MediaBuckets.All` at slice 6.1;
    // `MediaBucketTests` is the copy that fails the build when the server's set changes.
    expect(BUCKET_NAMES).toHaveLength(20)
    expect(new Set(BUCKET_NAMES).size).toBe(BUCKET_NAMES.length)
  })

  it.each(sourceFiles)('%s names no §7.1 bucket', (name, source) => {
    for (const bucket of BUCKET_NAMES) {
      expect(
        source.includes(`'${bucket}'`) || source.includes(`"${bucket}"`),
        `${name} names the bucket '${bucket}'; media/ takes its bucket as a prop so that every ` +
          'caller — expert, garage, broker and the public page — can reuse it unchanged',
      ).toBe(false)
    }
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

/**
 * design.md §3's public surface, on the browser side (slice 5.3).
 *
 * The server half of this boundary is architecture rule 2, and it is enforced by a build failure.
 * The browser half had nothing until now, and it is the half a customer actually meets: `api()`
 * attaches a bearer whenever `localStorage` holds one and **hard-navigates to `/login` on a 401**, so
 * a single convenient import here would send a member of the public — mid-form, holding their
 * identity documents — to a staff sign-in screen they have no account for. On a broker's own machine,
 * which is where anyone would first click the link to check it, it would also quietly attach that
 * broker's session to an anonymous request.
 *
 * So this module carries no credentials at all: not the token store, not the session helpers, and
 * not `api/client`'s request functions. `ApiError` is deliberately still allowed — it is a plain
 * error type carrying a status, with no fetch behaviour attached, and sharing it is what lets the
 * public page's error branches read like everybody else's.
 *
 * The role modules are banned for the reuse argument the first block makes, in reverse: this page
 * must not grow its own copy of anything, and it must not reach into a screen it can never render.
 */
describe('the public page carries no session', () => {
  const forbidden = [
    { pattern: /from\s+'(\.\.\/)+api\/tokens'/, what: 'the token store' },
    { pattern: /from\s+'(\.\.\/)+api\/session'/, what: 'the session helpers' },
    // Named imports from `api/client` other than the shared `ApiError` type — `api`, `apiBlob` and
    // anything else that fetches. Written as a lookahead so the allowed import stays allowed and a
    // second one added later is caught.
    {
      pattern: /from\s+'(\.\.\/)+api\/client'/,
      what: "api/client's request helpers",
      allow: /import\s*\{\s*ApiError\s*\}\s*from\s+'(\.\.\/)+api\/client'/,
    },
    { pattern: /from\s+'(\.\.\/)+expert\//, what: 'the expert module' },
    { pattern: /from\s+'(\.\.\/)+garage\//, what: 'the garage module' },
    { pattern: /from\s+'(\.\.\/)+officer\//, what: 'the officer module' },
    { pattern: /from\s+'(\.\.\/)+broker\//, what: 'the broker module' },
    { pattern: /from\s+'(\.\.\/)+admin\//, what: 'the admin module' },
    { pattern: /from\s+'(\.\.\/)+pages\//, what: 'a page' },
    { pattern: /from\s+'(\.\.\/)+push\//, what: 'the push module' },
  ]

  it('has source files to check', () => {
    // Non-vacuity, the same guard the two blocks above carry and the same one `Rule2_IsNotVacuous`
    // gives the server rules. Without it this whole block reads green on a renamed folder.
    expect(publicFiles.length).toBeGreaterThan(1)
  })

  it.each(publicFiles)('%s imports nothing that carries a session', (name, source) => {
    for (const { pattern, what, allow } of forbidden) {
      if (allow && allow.test(source)) continue
      expect(
        pattern.test(source),
        `${name} imports ${what}; the public page is unauthenticated (design.md §3) and must ` +
          'never attach a bearer or be redirected to /login',
      ).toBe(false)
    }
  })

  /**
   * The rule the ban above cannot express. `getTokens` is what `withBearer` calls, so naming it
   * directly catches a re-export or a relative path the patterns did not anticipate — the card asked
   * for this assertion specifically, and it is the one a reader will look for.
   */
  it.each(publicFiles)('%s never reads a stored token', (name, source) => {
    expect(source.includes('getTokens'), `${name} reads the token store`).toBe(false)
  })
})
