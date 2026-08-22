import { useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { clearTokens } from '../api/tokens'

/**
 * Sign out (pass 1 — **net-new**; nothing in the build had one).
 *
 * It exists because handing a shared workshop laptop to the next person is the normal case here, not
 * the edge one.
 *
 * **`queryClient.clear()` is the load-bearing half.** Dropping the tokens alone leaves every cached
 * `useQuery` in place, so the next person to sign in on the same browser sees the previous person's
 * worklist until each query refetches. `docs/demo-fix-list.md` #16 is the same shape of bug one layer
 * down — a browser holds one push subscription per *profile*, not per user — and two people sharing a
 * profile is exactly the case this is for.
 *
 * **No server-side revoke, deliberately.** design.md §4 revokes refresh tokens on rotation and on
 * deactivation; there is no sign-out endpoint, and inventing one is a change to the auth model rather
 * than a styling slice. The consequence is recorded rather than hidden: the refresh token stays valid
 * until `Auth.Jwt.RefreshTokenDays`, so this clears the session on *this device* and nothing more.
 */
export function useSignOut(): () => void {
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  return () => {
    clearTokens()
    queryClient.clear()
    navigate('/login', { replace: true })
  }
}
