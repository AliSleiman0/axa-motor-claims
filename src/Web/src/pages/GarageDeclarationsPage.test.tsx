import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DeclarationListItem } from '../garage/api'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import GarageDeclarationsPage from './GarageDeclarationsPage'

const NEWER: DeclarationListItem = {
  id: '00000000-0000-0000-0000-0000000d0002',
  state: 'approved',
  plateNo: 'PLC-TEST-D2',
  insuredName: 'PLACEHOLDER Insured 2',
  visaNo: 'PLACEHOLDER-VISA-0002',
  createdAt: '2026-08-22T10:00:00',
  submittedAt: '2026-08-22T10:30:00',
  decidedAt: '2026-08-22T11:00:00',
  mediaCount: 3,
}

/** A draft: no visa and no timestamps beyond creation, which is most of what G1 has to render. */
const DRAFT: DeclarationListItem = {
  id: '00000000-0000-0000-0000-0000000d0001',
  state: 'draft',
  plateNo: 'PLC-TEST-D1',
  insuredName: null,
  visaNo: null,
  createdAt: '2026-08-22T09:00:00',
  submittedAt: null,
  decidedAt: null,
  mediaCount: 0,
}

describe('G1 — declaration worklist', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(new Response(JSON.stringify([NEWER, DRAFT]), { status: 200 }))),
    )
  })

  it('renders each declaration in the order the server sent', async () => {
    show()

    const links = await screen.findAllByRole('link', { name: /^PLC-TEST/ })

    // §5.2 says newest-first and the server's ORDER BY is the only sort — a screen that imposed its
    // own would silently drift from it.
    expect(links.map((link) => link.textContent)).toEqual([NEWER.plateNo, DRAFT.plateNo])
    expect(links[0].getAttribute('href')).toBe(`/garage/${NEWER.id}`)
  })

  it('shows the status in words and the media count', async () => {
    show()

    expect(await screen.findByText('Approved')).toBeDefined()
    expect(screen.getByText('Draft')).toBeDefined()
    expect(screen.getByText('3')).toBeDefined()
  })

  it('renders a draft’s empty columns as em dashes rather than blanks', async () => {
    show()

    // A draft has no visa because no officer has approved it — that is a state, not missing data,
    // and a blank cell reads as a bug.
    await screen.findByText('Draft')
    expect(screen.getAllByText('—').length).toBeGreaterThan(0)
  })

  it('offers the way to create one', async () => {
    show()

    const link = await screen.findByRole('link', { name: 'New declaration' })
    expect(link.getAttribute('href')).toBe('/garage/new')
  })

  function show() {
    render(
      <TestQueryProvider>
        <MemoryRouter initialEntries={['/garage']}>
          <GarageDeclarationsPage />
        </MemoryRouter>
      </TestQueryProvider>,
    )
  }
})
