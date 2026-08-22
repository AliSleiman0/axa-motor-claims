import { useMe } from '../api/me'
import { Button } from './Button'
import { DesktopNav } from './DesktopNav'
import { useSignOut } from './useSignOut'
import type { RoleShell } from './roles'

/**
 * The one header, for all five roles (pass 1's `AppHeader`).
 *
 * The wordmark is **plain type, not a logo** — a placeholder, and the one place AXA's mark would
 * eventually sit. design.md §7's deliverable list excludes branding and AXA supplied none, so
 * inventing one here would be inventing client data on the most visible element in the product.
 *
 * The role is named because five different people use the same address, and the first thing support
 * asks is what the screen says at the top. The phone number is beside it for the same reason: it is
 * the identity this system logs in with (§9's phone-OTP), so it answers "which account am I on"
 * without a menu.
 *
 * Name and phone come from `/auth/me`. They are *not* read out of the JWT: the token carries `sub`
 * and `role` and no name, and the header renders perfectly well without them while that request is in
 * flight — which is why nothing here waits on it.
 */
export function AppHeader({ shell }: { shell: RoleShell }) {
  const { data: me } = useMe()
  const signOut = useSignOut()

  return (
    <header className="app-header">
      <span className="app-header__brand">AXA Motor Claims</span>
      <span className="app-header__role">{shell.name}</span>
      {shell.tabs.length > 0 ? <DesktopNav tabs={shell.tabs} /> : null}
      <span className="app-header__spacer" />
      {me ? <span className="app-header__phone mono">{me.phone}</span> : null}
      <Button variant="link" onClick={signOut}>
        Sign out
      </Button>
    </header>
  )
}
