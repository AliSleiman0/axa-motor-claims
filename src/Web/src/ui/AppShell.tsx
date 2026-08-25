import type { ReactNode } from 'react'
import { AppHeader } from './AppHeader'
import { shellFor } from './roles'

/**
 * The shell every authenticated screen sits in: header, then the page.
 *
 * **No phone bottom nav** (pass-2 decision 1b). It was drawn with two destinations — Claims and
 * Notifications — and the Notifications tab had no screen behind it; building one is net-new scope,
 * and with a single destination left a bar is not navigation. E1 and G1 are the recovery for a missed
 * popup, and back stays the in-page "← My claims" link that already carries the active search.
 *
 * `--control` is set here, once, by `app-shell--touch`: field roles get 48 px controls and office
 * roles 36 px, and every button and input inside inherits it. That is why no component in `ui/` has
 * to be told which kind of user is pressing it.
 */
export function AppShell({
  role,
  children,
  tabCounts,
}: {
  role: string | null
  children: ReactNode
  /**
   * Badge counts by tab path, for the header's nav. Passed in rather than fetched, because `ui/` may
   * not import a role module — the admin shell calls the hook and hands the number down (slice 6.2).
   */
  tabCounts?: Record<string, number>
}) {
  const shell = shellFor(role)

  return (
    <div className={`app-shell${shell.touch ? ' app-shell--touch' : ''}`}>
      <AppHeader shell={shell} tabCounts={tabCounts} />
      <main className="app-main">{children}</main>
    </div>
  )
}
