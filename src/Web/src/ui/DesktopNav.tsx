import { NavLink } from 'react-router-dom'
import { UNBUILT_TABS, type NavTab } from './roles'

/**
 * The office roles' tab bar (pass 1's `DesktopNav`).
 *
 * **One tab is not a navigation bar, and the bar stays anyway.** The officer and the broker have one
 * screen each today; keeping the bar means the shell is identical the day a second screen lands, and
 * that the three office roles are visibly the same product rather than three different ones.
 *
 * A tab whose route is not built yet renders as a plain disabled item rather than a link, and
 * `UNBUILT_TABS` is empty as of slice 6.2 — the mechanism stays for the next one.
 *
 * **Counts are passed in, never read here.** `ui/` may not import a role module, so the one number in
 * the chrome is fetched where it belongs (`AdminLayout`) and handed down. A tab with no entry, or an
 * entry of zero, renders exactly as it did before: nothing at all. That is not cosmetic — until 6.2
 * there was no endpoint counting failed pushes, and the rule then was that a zero we invented would
 * read as "nothing has failed", which is the one thing A2 exists to contradict. A real zero may say
 * it; an absent one may not.
 */
export function DesktopNav({ tabs, counts }: { tabs: NavTab[]; counts?: Record<string, number> }) {
  return (
    <nav className="desktop-nav" aria-label="Sections">
      {tabs.map((tab) => {
        if (UNBUILT_TABS.has(tab.to)) {
          return (
            <span key={tab.to} className="desktop-nav__tab" aria-disabled="true" title="Not built yet">
              {tab.label}
            </span>
          )
        }

        const count = counts?.[tab.to] ?? 0

        return (
          <NavLink key={tab.to} className="desktop-nav__tab" to={tab.to}>
            {tab.label}
            {count > 0 ? (
              // The number is announced, not just coloured: a red pill beside a word is invisible to
              // a screen reader and to anybody who cannot separate it from the tint behind it.
              <span className="desktop-nav__count">
                {count}
                <span className="visually-hidden"> failed</span>
              </span>
            ) : null}
          </NavLink>
        )
      })}
    </nav>
  )
}
