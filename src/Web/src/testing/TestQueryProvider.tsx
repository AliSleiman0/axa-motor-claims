import { useState, type ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'

/**
 * A QueryClient scoped to one mount, so no cache or in-flight request survives into the next test.
 * Retries are off: a test that asserts an error path should not wait for the production backoff.
 *
 * It is a component rather than a `renderWithClient` helper because `react-refresh` (a build error
 * here, via `--max-warnings 0`) allows a .tsx file to export components and nothing else.
 */
export function TestQueryProvider({ children }: { children: ReactNode }) {
  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: { retry: false },
          mutations: { retry: false },
        },
      }),
  )

  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}
