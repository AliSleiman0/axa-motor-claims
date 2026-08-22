import { useQuery } from '@tanstack/react-query'
import { api } from './client'

/** `/auth/me` — the signed-in user's own row. The JWT carries `sub` and `role` and no name. */
export interface Me {
  id: string
  phone: string
  role: string
  displayName: string
}

export function getMe(signal?: AbortSignal): Promise<Me> {
  return api<Me>('/auth/me', { signal })
}

/**
 * The query key is `['auth','me']` verbatim, because `useDecision` already fetches this exact
 * endpoint under that key when it stamps the officer's name into #18's approval image. Two keys for
 * one endpoint would mean the header and the approval image could disagree about who is signed in —
 * on the artifact that lands in AXA's claim folder.
 *
 * Moved here from `officer/api.ts` in slice 4.4: `AppHeader` is shared by every role, and `ui/` may
 * not import a role module.
 */
export const meKey = ['auth', 'me'] as const

export function useMe() {
  return useQuery({
    queryKey: meKey,
    queryFn: ({ signal }) => getMe(signal),
    // The name and phone in the header do not change while somebody is signed in.
    staleTime: Infinity,
  })
}
