import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { AssignmentListItem } from '../expert/api'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import ExpertAssignmentsPage from './ExpertAssignmentsPage'

const NEWER: AssignmentListItem = {
  id: '00000000-0000-0000-0000-00000000a002',
  visaNo: 'PLACEHOLDER-VISA-T00002',
  receivedAt: '2026-08-20T10:00:00',
  openedAt: null,
  arrivedAt: null,
  plateNo: 'PLC-TEST-T2',
  insuredName: 'PLACEHOLDER Insured T2',
  carMakeModel: 'PLACEHOLDER Make T2',
  accidentDate: '2026-08-19',
  mediaCount: 3,
}

/** The cold-cache case: an assignment that arrived while NEXT3 was down (§4). */
const COLD: AssignmentListItem = {
  id: '00000000-0000-0000-0000-00000000a001',
  visaNo: 'PLACEHOLDER-VISA-T00001',
  receivedAt: '2026-08-20T09:00:00',
  openedAt: null,
  arrivedAt: null,
  plateNo: null,
  insuredName: null,
  carMakeModel: null,
  accidentDate: null,
  mediaCount: 0,
}

describe('E1 — claim list', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(new Response(JSON.stringify([NEWER, COLD]), { status: 200 }))),
    )
  })

  it('renders each assignment with its media count, in the order the server sent', async () => {
    show()

    const links = await screen.findAllByRole('link')
    // §5.1 says newest-first, and the server's ORDER BY is the only sort — the screen must not
    // impose its own, or the two would silently drift apart.
    expect(links.map((link) => link.textContent)).toEqual([NEWER.visaNo, COLD.visaNo])
    expect(screen.getByRole('link', { name: NEWER.visaNo }).getAttribute('href')).toBe(
      `/expert/${NEWER.id}`,
    )
    expect(screen.getByText('3')).toBeDefined()
    // A cold cache renders as em dashes, not as blanks: no plate is a NEXT3 outage, not a car
    // without a number plate.
    expect(screen.getAllByText('—').length).toBeGreaterThan(0)
  })
})

describe('E1 — search (slice 3.2)', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn(() =>
      Promise.resolve(new Response(JSON.stringify([NEWER]), { status: 200 })),
    )
    vi.stubGlobal('fetch', fetchMock)
  })

  it('restores the term from the URL and asks the server for it', async () => {
    // The reload case, which is the whole reason the term lives in the URL rather than in state:
    // a phone that Android reclaimed under memory pressure comes back to the same search.
    show(`/expert?q=${encodeURIComponent(NEWER.plateNo!)}`)

    const input = await screen.findByLabelText('Search visa or plate')
    expect((input as HTMLInputElement).value).toBe(NEWER.plateNo)
    expect(requestedTerms(fetchMock)).toEqual([NEWER.plateNo])
  })

  it('debounces typing into a single request and writes the settled term to the URL', async () => {
    const user = userEvent.setup({ delay: null })
    show()

    const input = await screen.findByLabelText('Search visa or plate')
    await user.type(input, 'PLC')

    // Checked synchronously, before the debounce can have elapsed: the box already shows what was
    // typed, and nothing has been committed. This is the half that proves there is a delay at all.
    expect((input as HTMLInputElement).value).toBe('PLC')
    expect(requestedTerms(fetchMock)).toEqual([null])

    // Three keystrokes, exactly one extra request — not one per character, on the connection that
    // is this application's scarce resource. An undebounced version fails on the length here.
    await waitFor(() => {
      expect(requestedTerms(fetchMock)).toEqual([null, 'PLC'])
    })
    expect(screen.getByTestId('location').textContent).toBe('?q=PLC')
  })

  it('clears the term out of the URL rather than leaving an empty one', async () => {
    const user = userEvent.setup({ delay: null })
    show('/expert?q=PLC')

    await user.clear(await screen.findByLabelText('Search visa or plate'))

    await waitFor(() => {
      expect(screen.getByTestId('location').textContent).toBe('')
    })
  })

  it('explains the cold-cache limitation only while a term is active', async () => {
    // §5.1's search matches the plate on the *cached* claim, so an assignment NEXT3 has never
    // answered for cannot be found by plate. Saying so beats an expert deciding search is broken.
    show()
    expect(await screen.findAllByRole('link')).toHaveLength(1)
    expect(screen.queryByText(/has no plate to match/)).toBeNull()

    show('/expert?q=PLC')
    expect(await screen.findByText(/has no plate to match/)).toBeDefined()
  })

  it('distinguishes "nothing matched" from "nothing assigned"', async () => {
    fetchMock.mockImplementation(() =>
      Promise.resolve(new Response(JSON.stringify([]), { status: 200 })),
    )

    show('/expert?q=PLC-NOPE')
    expect(await screen.findByText(/No claim of yours matches/)).toBeDefined()
    // The old copy would have told an expert with a full worklist that they have no claims.
    expect(screen.queryByText('No claims assigned yet.')).toBeNull()

    show()
    expect(await screen.findByText('No claims assigned yet.')).toBeDefined()
  })

  it('carries the active search into each claim link', async () => {
    // So E2 can hand it back on its way out. Found in the browser pass, not by the suite: an expert
    // who searches, opens a claim and returns had to retype the plate, which is most of the value
    // of having searched. Nothing throws and every other assertion still passed.
    show('/expert?q=PLC-TEST-T2')

    const link = await screen.findByRole('link', { name: NEWER.visaNo })
    expect(link.getAttribute('href')).toBe(`/expert/${NEWER.id}?q=PLC-TEST-T2`)
  })

  it('leaves the claim link clean when nothing is being searched', async () => {
    show()

    const link = await screen.findByRole('link', { name: NEWER.visaNo })
    expect(link.getAttribute('href')).toBe(`/expert/${NEWER.id}`)
  })

  it('keeps the search box mounted while a new term is loading', async () => {
    // The input sits above every loading/error branch on purpose: an early return would unmount it
    // mid-typing and take the caret with it. Never renders in a test that only checks text.
    let release: (value: Response) => void = () => {}
    fetchMock.mockImplementation(
      () =>
        new Promise<Response>((resolve) => {
          release = resolve
        }),
    )

    show()

    expect(await screen.findByLabelText('Search visa or plate')).toBeDefined()
    expect(screen.getByText('Loading claims…')).toBeDefined()
    release(new Response(JSON.stringify([NEWER]), { status: 200 }))
  })
})

/** Renders `useLocation().search`, so a test can assert what the URL round-trip actually wrote. */
function LocationProbe() {
  return <span data-testid="location">{useLocation().search}</span>
}

/**
 * A 30 ms debounce rather than the production 300 ms, and rather than zero. Zero would make the
 * "nothing committed yet" assertion above vacuous; fake timers deadlock against @testing-library's
 * async helpers, which is what the injectable exists to avoid.
 */
function show(entry = '/expert') {
  render(
    <TestQueryProvider>
      <MemoryRouter initialEntries={[entry]}>
        <ExpertAssignmentsPage debounceMs={30} />
        <LocationProbe />
      </MemoryRouter>
    </TestQueryProvider>,
  )
}

/** The `q` of every list request made so far, in order; `null` where none was sent. */
function requestedTerms(fetchMock: ReturnType<typeof vi.fn>): (string | null)[] {
  return fetchMock.mock.calls.map(([url]) =>
    new URL(String(url), 'http://localhost').searchParams.get('q'),
  )
}
