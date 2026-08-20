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

/** Where a signed-in user lands. Roles with no screen of their own yet fall back to admin. */
export function homePathFor(role: string | null): string {
  return role === 'expert' ? '/expert' : '/admin/experts'
}
