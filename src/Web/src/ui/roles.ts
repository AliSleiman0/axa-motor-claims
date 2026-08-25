/**
 * What each role's shell says and shows.
 *
 * Keyed by the **server's** role strings verbatim (`UserRoles`, check-constrained on `app_user.role`)
 * — snake case, so `claim_officer` and not `claimOfficer`. `api/session.ts` makes the same point about
 * `HOME_PATHS`, and for the same reason: a typo here is a garage looking at a header that names
 * somebody else's job.
 *
 * A `.ts` file rather than living beside the components, for `push/copy.ts`'s reason —
 * `react-refresh/only-export-components` allows a `.tsx` file to export components and nothing else.
 */

export interface NavTab {
  to: string
  label: string
}

export interface RoleShell {
  /** Named in the header because five different people use the same address. */
  name: string
  /** Field roles get 48 px controls and no desktop nav; office roles get 36 px and a tab bar. */
  touch: boolean
  tabs: NavTab[]
  /**
   * What this role calls its landing screen, for a button that has to name a destination rather
   * than point at one — S1's "Go to My claims" after activation. `homePathFor` gives the path; this
   * gives the words, and the two are deliberately separate because the path is a contract with
   * §8's push URLs while the label is only ever read by a person.
   */
  home: string
}

/**
 * The admin's four profile tabs, from the same list the admin pages route on.
 *
 * `PROFILE_KINDS` lives in `admin/kinds.ts` and `ui/` may not import a role module, so the four slugs
 * are repeated here — deliberately, and the cost is one build failure away from being noticed, since
 * `ProfileListPage` renders "Unknown profile type." for a slug `findKind` does not know.
 */
const ADMIN_TABS: NavTab[] = [
  { to: '/admin/experts', label: 'Experts' },
  { to: '/admin/garages', label: 'Garages' },
  { to: '/admin/claim-officers', label: 'Claim officers' },
  { to: '/admin/brokers', label: 'Brokers' },
  /*
   * Live since slice 6.2, and it carries the one count in the chrome. Pass 3 argued that place: nobody
   * opens A2 on a hunch, so if a failure never announces itself it is never read — and a failed push
   * is a photograph AXA does not have.
   */
  { to: '/admin/failed-pushes', label: 'Failed pushes' },
]

export const ROLE_SHELLS: Record<string, RoleShell> = {
  expert: { name: 'Expert', touch: true, tabs: [], home: 'My claims' },
  garage: { name: 'Garage', touch: true, tabs: [], home: 'My declarations' },
  claim_officer: {
    name: 'Claim officer',
    touch: false,
    tabs: [{ to: '/officer', label: 'Inbox' }],
    home: 'Declarations to review',
  },
  // 5.2 built B1, and the tab is live. The three office roles are visibly the same product.
  broker: { name: 'Broker', touch: false, tabs: [{ to: '/broker', label: 'Requests' }], home: 'Requests' },
  admin: { name: 'Admin', touch: false, tabs: ADMIN_TABS, home: 'Experts' },
}

/**
 * Routes that are not built yet, so their tab renders as a plain disabled item.
 *
 * Empty since slice 6.2 shipped A2 — kept, because the next tab drawn ahead of its screen wants it,
 * and because deleting a guard is how the next one comes back as a link to a blank page.
 * `AppHeader.test.tsx` exercises the branch by adding a path to this set, so an empty default does
 * not quietly become untested code.
 */
export const UNBUILT_TABS = new Set<string>()

export function shellFor(role: string | null): RoleShell {
  // `api/session.ts` falls back to the admin screens for a role it cannot read, and this follows it:
  // the header must name *something*, and the API answers 403 to anything that role may not see.
  return (role ? ROLE_SHELLS[role] : undefined) ?? ROLE_SHELLS.admin
}
