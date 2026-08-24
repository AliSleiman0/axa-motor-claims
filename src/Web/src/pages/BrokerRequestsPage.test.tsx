import { act, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import type { BrokerRequestListItem, BrokerRequestState } from '../broker/api'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import BrokerRequestsPage from './BrokerRequestsPage'

let fetchMock: ReturnType<typeof vi.fn>

function row(overrides: Partial<BrokerRequestListItem> = {}): BrokerRequestListItem {
  return {
    id: `00000000-0000-0000-0000-0000000b${(counter++).toString().padStart(4, '0')}`,
    option: 1,
    state: 'draft',
    insuredName: 'PLACEHOLDER Insured Three',
    insuranceType: 'MOTOR ALL RISK',
    customerMobile: null,
    createdAt: '2026-08-22T15:02:00',
    submittedAt: null,
    emailedAt: null,
    emailRecipient: null,
    linkExpiresAt: null,
    documentCount: 0,
    ...overrides,
  }
}

let counter = 1

describe('B1 — the broker worklist', () => {
  it('renders all seven states with the artboard labels', async () => {
    const states: BrokerRequestState[] = [
      'draft',
      'submitted',
      'link_issued',
      'customer_in_progress',
      'ready_to_send',
      'sent',
      'expired',
    ]

    // A submitted row whose email *did* go, so this test reads the plain label rather than the
    // unsent variant the Resend test covers.
    show(states.map((state) => row({ state, emailedAt: '2026-08-22T15:00:00' })))

    for (const label of [
      'Draft',
      'Submitted',
      'Link issued',
      'Customer in progress',
      'Ready to send',
      'Sent',
      'Expired',
    ]) {
      expect(await screen.findByText(label)).toBeTruthy()
    }
  })

  it('gives Ready to send the only green chip', async () => {
    show([
      row({ state: 'ready_to_send', emailedAt: null }),
      row({ state: 'sent', emailedAt: '2026-08-22T15:00:00' }),
      row({ state: 'draft' }),
    ])

    // The tone is what the artboard is specific about: green means "something is waiting on you",
    // and three of the seven rows are waiting on somebody else.
    expect((await screen.findByText('Ready to send')).className).toContain('chip--ok')
    expect(screen.getByText('Sent').className).not.toContain('chip--ok')
    expect(screen.getByText('Draft').className).not.toContain('chip--ok')
  })

  it('offers one action per row, and only where there is one', async () => {
    show([
      row({ state: 'draft' }),
      row({ state: 'ready_to_send' }),
      row({ state: 'expired' }),
      row({ state: 'customer_in_progress' }),
      row({ state: 'sent', emailedAt: '2026-08-22T15:00:00' }),
    ])

    expect(await screen.findByRole('link', { name: 'Continue' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Review' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Reissue' })).toBeTruthy()

    // The two rows waiting on somebody else offer nothing at all.
    const cells = screen.getAllByRole('cell')
    const empty = cells.filter((cell) => cell.textContent === '')
    expect(empty.length).toBeGreaterThanOrEqual(2)
  })

  it('shows an Option 2 row that has nothing yet as em dashes rather than blanks', async () => {
    show([row({ state: 'link_issued', option: 2, insuredName: null, insuranceType: null })])

    const cells = await screen.findAllByRole('cell')
    expect(cells[0].textContent).toBe('—')
    expect(cells[1].textContent).toBe('—')
    expect(cells[2].textContent).toBe('2')
  })

  it('offers Resend on a submitted request whose email has not gone', async () => {
    show([row({ state: 'submitted', submittedAt: '2026-08-22T15:00:00', emailedAt: null })])

    expect(await screen.findByRole('button', { name: 'Resend' })).toBeTruthy()
    expect(screen.getByText('Email not yet sent')).toBeTruthy()
  })

  it('does not offer Resend once the email has gone', async () => {
    show([
      row({
        state: 'submitted',
        submittedAt: '2026-08-22T15:00:00',
        emailedAt: '2026-08-22T15:01:00',
      }),
    ])

    await screen.findByText('Submitted')
    expect(screen.queryByRole('button', { name: 'Resend' })).toBeNull()
  })

  it('sends exactly one resend for three taps in the same tick', async () => {
    show([row({ state: 'submitted', submittedAt: '2026-08-22T15:00:00', emailedAt: null })])

    const resend = await screen.findByRole('button', { name: 'Resend' })

    // Raw `.click()` inside one `act`, never `fireEvent`: `fireEvent` flushes React between events,
    // so by the second call `pending` is true and the `disabled` attribute swallows the rest — a
    // real guard, but not the latch this asserts. 4.2's note (2).
    await act(async () => {
      resend.click()
      resend.click()
      resend.click()
    })

    await waitFor(() => {
      expect(postsTo('/resend')).toBe(1)
    })
  })

  it('names the two ways in', async () => {
    show([])

    const head = (await screen.findByRole('heading', { name: 'Requests' })).closest('div')!
    expect(within(head).getByRole('link', { name: 'New request' })).toBeTruthy()
    expect(within(head).getByRole('link', { name: 'Send customer link' })).toBeTruthy()
  })
})

function postsTo(suffix: string): number {
  return fetchMock.mock.calls.filter(([url, init]) => {
    const request = init as RequestInit | undefined
    return String(url).endsWith(suffix) && request?.method === 'POST'
  }).length
}

function show(rows: BrokerRequestListItem[]) {
  fetchMock = vi.fn((url: string) => {
    const path = String(url)
    if (path.endsWith('/resend')) {
      return Promise.resolve(json({ state: 'submitted', emailFailed: false, recipient: 'x@y.invalid' }))
    }
    return Promise.resolve(json(rows))
  })
  vi.stubGlobal('fetch', fetchMock)

  render(
    <TestQueryProvider>
      <MemoryRouter initialEntries={['/broker']}>
        <Routes>
          <Route path="/broker" element={<BrokerRequestsPage />} />
        </Routes>
      </MemoryRouter>
    </TestQueryProvider>,
  )
}

function json(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200 })
}
