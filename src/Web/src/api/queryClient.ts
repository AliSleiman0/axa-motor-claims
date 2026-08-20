import { QueryClient } from '@tanstack/react-query'
import { ApiError } from './client'

/**
 * One QueryClient for the app, and a fresh one per test.
 *
 * Retrying an ApiError is pointless: `api()` already refreshes once on a 401 and hard-navigates to
 * /login when that fails, and a 404 or a 400 will not fix itself. What is worth a second go is a
 * transport failure — the flaky-signal case an expert at a crash site actually hits.
 */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: (failureCount, error) => !(error instanceof ApiError) && failureCount < 2,
        // E2's GET stamps opened_at on the server. Refetching it every time the window regains
        // focus is chatter, on a connection that is the scarce resource in this whole application.
        refetchOnWindowFocus: false,
      },
      mutations: { retry: false },
    },
  })
}
