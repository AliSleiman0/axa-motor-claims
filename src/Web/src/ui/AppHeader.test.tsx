import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getTokens, setTokens } from '../api/tokens'
import { AppShell } from './AppShell'
import { UNBUILT_TABS } from './roles'

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
  it('gives the admin five live tabs, Failed pushes among them', () => {
    // Was "four profile tabs plus Failed pushes", which was pinned while A2 had no route. Slice 6.2
    // built it, so the fifth tab joins the loop rather than being asserted as text beside it.
    show('admin')

    for (const label of ['Experts', 'Garages', 'Claim officers', 'Brokers', 'Failed pushes']) {
      expect(screen.getByRole('link', { name: new RegExp(`^${label}`) })).toBeDefined()
    }
  })

  it('shows a count on a tab when it is given one', () => {
    show('admin', { '/admin/failed-pushes': 2 })

    const tab = screen.getByRole('link', { name: /^Failed pushes/ })
    expect(tab.textContent).toContain('2')
    // Announced, not merely coloured: a red pill beside a word says nothing to a screen reader.
    expect(tab.textContent).toContain('failed')
  })

  it('shows nothing at zero, so an invented number cannot read as reassurance', () => {
    // The rule slice 4.4 recorded, kept now that the endpoint is real: a count of nothing must not
    // appear on the tab, because "0" beside Failed pushes reads as "nothing has failed" — which is
    // the one thing that screen exists to be able to contradict. A *real* zero may say it, on the
    // screen, with a timestamp; the chrome may not.
    show('admin', { '/admin/failed-pushes': 0 })

    expect(screen.getByRole('link', { name: /^Failed pushes/ }).textContent).toBe('Failed pushes')
  })

  it('still renders an unbuilt tab as a muted non-link', () => {
    // `UNBUILT_TABS` is empty as of 6.2 and the mechanism is kept for the next tab drawn ahead of its
    // screen. An empty set would make this branch dead code that nothing covers — CLAUDE.md's "a
    // guard that quietly stops covering new code is worse than none" — so the test puts a path in
    // and takes it out again rather than letting the branch rot.
    UNBUILT_TABS.add('/admin/brokers')
    try {
      show('admin')

      expect(screen.queryByRole('link', { name: 'Brokers' })).toBeNull()
      expect(screen.getByText('Brokers').getAttribute('aria-disabled')).toBe('true')
    } finally {
      UNBUILT_TABS.delete('/admin/brokers')
    }
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
function show(role: string, tabCounts?: Record<string, number>): QueryClient {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/start']}>
        <Routes>
          <Route
            path="/start"
            element={
              <AppShell role={role} tabCounts={tabCounts}>
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
