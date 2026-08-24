import { getTokens } from './tokens'

/**
 * The role claim out of the access token, **for navigation only** — which landing screen to show
 * after sign-in, and which layout to render.
 *
 * Every authorization decision is the server's (design.md §9: one policy per endpoint group,
 * enforced per request). A token edited in devtools buys nothing here but a screen whose every call
 * comes back 403, so this is a routing convenience and not a control.
 */
export function currentRole(): string | null {
  const tokens = getTokens()
  if (!tokens) return null

  const payload = tokens.accessToken.split('.')[1]
  if (!payload) return null

  try {
    // base64url → base64. atob tolerates the missing padding.
    const claims = JSON.parse(atob(payload.replace(/-/g, '+').replace(/_/g, '/'))) as {
      role?: unknown
    }
    return typeof claims.role === 'string' ? claims.role : null
  } catch {
    // A token we cannot read is a token we route as "no role" — the guard below sends them to the
    // admin pages, whose API calls will 401 and bounce them to /login.
    return null
  }
}

/**
 * Where a signed-in user lands. Roles with no screen of their own yet fall back to admin.
 *
 * The keys are the server's role strings verbatim (`UserRoles`, check-constrained on `app_user.role`)
 * — snake case, so `claim_officer` and not `claimOfficer`. A typo here is a garage silently landing
 * on the admin screens and meeting a wall of 403s, which is what §5.2's two roles did until slice 4.2.
 *
 * Every role has a home now: slice 5.2 built B1, so `broker` no longer falls through to the
 * admin's screens.
 */
const HOME_PATHS: Record<string, string> = {
  expert: '/expert',
  garage: '/garage',
  claim_officer: '/officer',
  broker: '/broker',
}

export function homePathFor(role: string | null): string {
  return (role && HOME_PATHS[role]) ?? '/admin/experts'
}
