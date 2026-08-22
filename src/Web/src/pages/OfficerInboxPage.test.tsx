import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { OfficerDeclarationListItem } from '../officer/api'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import OfficerInboxPage from './OfficerInboxPage'

/** Waiting longest — the server sends this one first, because an officer works a queue. */
const OLDEST: OfficerDeclarationListItem = {
  id: '00000000-0000-0000-0000-0000000d0001',
  state: 'submitted',
  plateNo: 'PLC-TEST-D1',
  insuredName: 'PLACEHOLDER Insured 1',
  garageName: 'PLACEHOLDER Garage One',
  garageEmail: 'PLACEHOLDER-one@example.invalid',
  garagePhone: '+999000008001',
  createdAt: '2026-08-22T08:00:00',
  submittedAt: '2026-08-22T08:30:00',
  mediaCount: 2,
}

/** A garage with no profile row — the left join's case, and a real one after an incomplete invite. */
const NEWER: OfficerDeclarationListItem = {
  id: '00000000-0000-0000-0000-0000000d0002',
  state: 'submitted',
  plateNo: 'PLC-TEST-D2',
  insuredName: null,
  garageName: null,
  garageEmail: null,
  garagePhone: null,
  createdAt: '2026-08-22T09:00:00',
  submittedAt: '2026-08-22T09:30:00',
  mediaCount: 5,
}

describe('O1 — declaration inbox', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(new Response(JSON.stringify([OLDEST, NEWER]), { status: 200 }))),
    )
  })

  it('keeps the server’s oldest-first order', async () => {
    show()

    const links = await screen.findAllByRole('link', { name: /^PLC-TEST/ })

    // Deliberately the opposite of every other list in the app. §5.2 has an officer working a queue,
    // so the declaration that has waited longest comes first — and the screen does not re-sort, for
    // the same reason E1 does not: two orderings that can disagree is worse than either.
    expect(links.map((link) => link.textContent)).toEqual([OLDEST.plateNo, NEWER.plateNo])
    expect(links[0].getAttribute('href')).toBe(`/officer/${OLDEST.id}`)
  })

  it('shows the garage contact the officer needs to chase it', async () => {
    show()

    expect(await screen.findByText('PLACEHOLDER Garage One')).toBeDefined()
    expect(screen.getByText('+999000008001')).toBeDefined()
    expect(screen.getByText('5')).toBeDefined()
  })

  it('renders a missing garage profile as an em dash', async () => {
    show()

    await screen.findByText('PLACEHOLDER Garage One')
    expect(screen.getAllByText('—').length).toBeGreaterThan(0)
  })

  it('says so when nothing is waiting', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(new Response('[]', { status: 200 }))),
    )
    show()

    expect(await screen.findByText('Nothing is waiting for review.')).toBeDefined()
  })

  function show() {
    render(
      <TestQueryProvider>
        <MemoryRouter initialEntries={['/officer']}>
          <OfficerInboxPage />
        </MemoryRouter>
      </TestQueryProvider>,
    )
  }
})
