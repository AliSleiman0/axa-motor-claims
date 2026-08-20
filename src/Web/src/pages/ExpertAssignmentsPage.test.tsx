import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
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
    render(
      <TestQueryProvider>
        <MemoryRouter>
          <ExpertAssignmentsPage />
        </MemoryRouter>
      </TestQueryProvider>,
    )

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
