import { NavLink } from 'react-router-dom'
import { UNBUILT_TABS, type NavTab } from './roles'

/**
 * The office roles' tab bar (pass 1's `DesktopNav`).
 *
 * **One tab is not a navigation bar, and the bar stays anyway.** The officer and the broker have one
 * screen each today; keeping the bar means the shell is identical the day a second screen lands, and
 * that the three office roles are visibly the same product rather than three different ones.
 *
 * A tab whose route is not built yet renders as a plain disabled item rather than a link — pass 3
 * puts "Failed pushes" in the admin nav, but A2 is slice 6.2, and a tab that navigates to a blank
 * screen is worse than one that says it is not ready. Its count is deliberately absent for the same
 * reason: **there is no endpoint that counts failed rows yet**, and a zero we made up would be read
 * as "nothing has failed".
 */
export function DesktopNav({ tabs }: { tabs: NavTab[] }) {
  return (
    <nav className="desktop-nav" aria-label="Sections">
      {tabs.map((tab) =>
        UNBUILT_TABS.has(tab.to) ? (
          <span key={tab.to} className="desktop-nav__tab" aria-disabled="true" title="Not built yet">
            {tab.label}
          </span>
        ) : (
          <NavLink key={tab.to} className="desktop-nav__tab" to={tab.to}>
            {tab.label}
          </NavLink>
        ),
      )}
    </nav>
  )
}
