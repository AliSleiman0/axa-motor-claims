import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getTokens, setTokens } from '../api/tokens'
import { AppShell } from './AppShell'

const ME = {
  id: '00000000-0000-0000-0000-0000000000aa',
  phone: '+999000000001',
  role: 'claim_officer',
  displayName: 'PLACEHOLDER Officer One',
}

beforeEach(() => {
  localStorage.clear()
  setTokens({ accessToken: 'PLACEHOLDER.access', refreshToken: 'PLACEHOLDER.refresh' })
  vi.stubGlobal(
    'fetch',
    vi.fn(() => Promise.resolve(new Response(JSON.stringify(ME), { status: 200 }))),
  )
})

describe('AppHeader — Sign out (slice 4.4, net-new)', () => {
  it('names the role and shows the signed-in phone number', async () => {
    show('claim_officer')

    expect(screen.getByText('Claim officer')).toBeDefined()
    // From `/auth/me`, not from the token: the JWT carries `sub` and `role` and no phone.
    expect(await screen.findByText(ME.phone)).toBeDefined()
  })

  it('renders the header before /auth/me answers', () => {
    // Nothing in the chrome waits on that request. A header that blinked in after a round trip would
    // shift the whole page under an expert's thumb on every navigation.
    show('expert')

    expect(screen.getByText('AXA Motor Claims')).toBeDefined()
    expect(screen.getByText('Expert')).toBeDefined()
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeDefined()
  })

  it('clears the tokens and goes to /login', async () => {
    const user = userEvent.setup()
    show('claim_officer')

    await user.click(screen.getByRole('button', { name: 'Sign out' }))

    await waitFor(() => {
      expect(screen.getByTestId('location').textContent).toBe('/login')
    })
    expect(getTokens()).toBeNull()
  })

  it('empties the query cache, so the next person does not see this one’s data', async () => {
    // The half that is easy to leave out. Dropping the tokens alone leaves every cached `useQuery`
    // in place, so the next sign-in on the same browser renders the previous user's worklist until
    // each query refetches — and `docs/demo-fix-list.md` #16 is the same shape of bug one layer down,
    // where two people share one browser profile and the second is told somebody else's state.
    const user = userEvent.setup()
    const client = show('claim_officer')

    await screen.findByText(ME.phone)
    expect(client.getQueryCache().getAll().length).toBeGreaterThan(0)

    await user.click(screen.getByRole('button', { name: 'Sign out' }))

    await waitFor(() => {
      expect(client.getQueryCache().getAll()).toHaveLength(0)
    })
  })
})

describe('DesktopNav', () => {
  it('gives the admin its four profile tabs plus Failed pushes', () => {
    show('admin')

    for (const label of ['Experts', 'Garages', 'Claim officers', 'Brokers']) {
      expect(screen.getByRole('link', { name: label })).toBeDefined()
    }
    expect(screen.getByText('Failed pushes')).toBeDefined()
  })

  it('renders Failed pushes as a non-link, with no count', () => {
    // A2 is slice 6.2. A tab that navigated to a blank screen would be worse than one that says it
    // is not ready — and a count is deliberately absent, because no endpoint counts failed rows yet
    // and a zero we invented would read as "nothing has failed".
    show('admin')

    expect(screen.queryByRole('link', { name: 'Failed pushes' })).toBeNull()
    expect(screen.getByText('Failed pushes').getAttribute('aria-disabled')).toBe('true')
  })

  it('keeps a one-tab bar for the officer', () => {
    // One tab is not navigation, and it stays anyway so the shell is identical the day a second
    // screen lands — and so the three office roles are visibly the same product.
    show('claim_officer')

    expect(screen.getByRole('link', { name: 'Inbox' })).toBeDefined()
  })

  it('gives a field role no nav at all', () => {
    // pass-2 decision 1b: the phone bar was drawn with two destinations and the second had no screen
    // behind it, so with one left there is no bar. Its own test rather than a second `show()` inside
    // the one above — two renders share a document, and the first officer's nav would satisfy this.
    show('expert')

    expect(screen.queryByRole('navigation')).toBeNull()
  })
})

/** Returns the client so a test can assert what sign-out did to the cache. */
function show(role: string): QueryClient {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/start']}>
        <Routes>
          <Route
            path="/start"
            element={
              <AppShell role={role}>
                <p>PLACEHOLDER page</p>
              </AppShell>
            }
          />
          <Route path="/login" element={<p>PLACEHOLDER login</p>} />
        </Routes>
        <LocationProbe />
      </MemoryRouter>
    </QueryClientProvider>,
  )

  return client
}

function LocationProbe() {
  return <span data-testid="location">{useLocation().pathname}</span>
}
