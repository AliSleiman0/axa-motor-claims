import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import AdminFailedPushesPage from './AdminFailedPushesPage'
import { useOutboxFailedCount } from '../admin/outbox'
import { TestQueryProvider } from '../testing/TestQueryProvider'

/**
 * A2 — the failed-push queue (design.md §5.4, the pass-3 A2Queue and A2Empty artboards).
 *
 * Timestamps arrive from the API without a zone, exactly as SQL Server hands them over, because that
 * is what the page has to cope with: `toUtcDate` is the difference between "next attempt in ~2 h" and
 * an estimate hours out.
 */
const FAILED = {
  id: '00000000-0000-0000-0000-00000000f001',
  operation: 'upload_document',
  visaNo: 'PLACEHOLDER-VISA-0003',
  status: 'failed',
  attempts: 8,
  lastError: 'NEXT3 rejected the document type PLACEHOLDER-DOC-02.',
  createdAt: '2026-08-21T09:12:00',
  lastAttemptAt: '2026-08-22T11:48:00',
  nextRetryAt: '2026-08-22T11:48:00',
}

const STILL_TRYING = {
  id: '00000000-0000-0000-0000-00000000f002',
  operation: 'push_approval',
  visaNo: 'PLACEHOLDER-VISA-0004',
  status: 'pending',
  attempts: 4,
  lastError: 'Timed out.',
  createdAt: '2026-08-22T06:02:00',
  lastAttemptAt: null,
  nextRetryAt: futureIso(2 * 60 * 60 * 1000),
}

let calls: string[]

describe('A2 — failed pushes', () => {
  beforeEach(() => {
    calls = []
    stubFetch([FAILED, STILL_TRYING])
  })

  it('shows the operation and the visa raw, in mono', async () => {
    show()

    // An admin reads these two out to a developer, so they are never prettified into "Upload
    // document" — the string in the database is the string on the screen.
    const operation = await screen.findByText('upload_document')
    expect(operation.closest('td')?.className).toContain('mono')
    expect(screen.getByText('PLACEHOLDER-VISA-0003').closest('td')?.className).toContain('mono')

    expect(screen.getByText('NEXT3 rejected the document type PLACEHOLDER-DOC-02.')).toBeDefined()
    expect(screen.getByText('8')).toBeDefined()
  })

  it('tells a row that has given up from one that is still going', async () => {
    show()

    // Red is failed: it will sit there for ever until somebody presses Retry. Amber has not failed,
    // it is just taking long enough that somebody would otherwise wonder where the photograph went.
    const failed = await screen.findByText('Failed')
    expect(failed.className).toContain('chip--danger')
    expect(screen.getByText('Still trying').className).toContain('chip--warn')

    expect(screen.getByText(/next attempt in ~2 h/)).toBeDefined()
  })

  it('says "Retry" on a dead row and "Retry now" on a live one', async () => {
    show()

    // Same endpoint either way; the label differs because the two rows mean different things — one is
    // being resurrected, the other brought forward.
    expect(await screen.findByRole('button', { name: 'Retry' })).toBeDefined()
    expect(screen.getByRole('button', { name: 'Retry now' })).toBeDefined()
  })

  it('shows an em dash where a row has never been tried', async () => {
    show()

    // `last_attempt_at` is null on rows that predate slice 6.2 and on rows never claimed. Inventing a
    // time from `created_at` would be reporting an attempt that never happened.
    await screen.findByText('upload_document')
    const stillTryingRow = screen.getByText('PLACEHOLDER-VISA-0004').closest('tr')
    expect(stillTryingRow?.textContent).toContain('—')
  })

  it('retries one row and refetches the list', async () => {
    show()
    await screen.findByRole('button', { name: 'Retry' })

    await userEvent.click(screen.getByRole('button', { name: 'Retry' }))

    await waitFor(() => {
      expect(calls).toContain(`POST /api/admin/outbox/${FAILED.id}/retry`)
    })
    await waitFor(() => {
      expect(calls.filter((call) => call === 'GET /api/admin/outbox').length).toBeGreaterThan(1)
    })
  })

  it('refreshes the nav badge too, not just the table', async () => {
    // The badge is mounted by `AdminLayout`, not by this page, so it is rendered alongside here —
    // invalidating a key nothing is observing only marks it stale, and a test without the badge
    // present would pass whether the mutation invalidated that key or not. Leaving the count stale is
    // the real bug: a number in the chrome contradicting the screen the admin has just cleared.
    show(<CountProbe />)
    await screen.findByRole('button', { name: 'Retry' })
    const before = calls.filter((call) => call === 'GET /api/admin/outbox/count').length

    await userEvent.click(screen.getByRole('button', { name: 'Retry' }))

    await waitFor(() => {
      expect(calls.filter((call) => call === 'GET /api/admin/outbox/count').length)
        .toBeGreaterThan(before)
    })
  })

  it('retries everything in one call rather than one call per row', async () => {
    show()
    await screen.findByRole('button', { name: 'Retry all' })

    await userEvent.click(screen.getByRole('button', { name: 'Retry all' }))

    await waitFor(() => {
      expect(calls).toContain('POST /api/admin/outbox/retry-all')
    })
    // One statement over the same rows the screen shows — not a loop over the visible ones.
    expect(calls.filter((call) => call.includes('/retry'))).toHaveLength(1)
  })

  it('explains a refusal in a sentence rather than showing the code', async () => {
    stubFetch([FAILED], { retryStatus: 409, retryBody: '{"error":"not_retryable"}' })
    show()
    await screen.findByRole('button', { name: 'Retry' })

    await userEvent.click(screen.getByRole('button', { name: 'Retry' }))

    expect(
      await screen.findByText(
        'That push is not waiting any more — it has either been sent or is being sent now.',
      ),
    ).toBeDefined()
  })

  it('reassures when nothing has failed, and says when it last looked', async () => {
    stubFetch([])
    show()

    // Empty is the normal state, so it is drawn as an answer rather than as a blank table — and the
    // timestamp is the point of it: "nothing has failed" has to be distinguishable from "this page
    // has not loaded", which on a monitoring screen is the whole value of an empty state.
    expect(await screen.findByText('Nothing failed.')).toBeDefined()
    expect(screen.getByText(/Last checked /)).toBeDefined()

    // Nothing to retry, so nothing offering to.
    expect(screen.queryByRole('button', { name: 'Retry all' })).toBeNull()
  })

  it('says so when the queue cannot be loaded, with the status', async () => {
    // The one case the empty state must never be confused with.
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(new Response('nope', { status: 503 }))),
    )
    show()

    expect(await screen.findByText(/Could not load the queue \(503\)\./)).toBeDefined()
    expect(screen.queryByText('Nothing failed.')).toBeNull()
  })
  /**
   * **The screen has three states, not two, and the browser pass is what caught it.** A `pending`
   * row is listed for either of two opposite reasons — its next attempt is far *ahead* (working
   * through a backoff, will clear itself) or far *behind* (nothing is claiming it, will not). Both
   * rendered as amber "Still trying / next attempt due now", so the row that needs somebody to act
   * was the one reassuring them, which is the failure A2 exists to prevent one level down.
   */
  it('says an overdue row is not moving, rather than that it is still trying', async () => {
    stubFetch([{ ...STILL_TRYING, nextRetryAt: futureIso(-20 * 60 * 1000) }], { overdue: 1 })

    show()

    expect(await screen.findByText('Not moving')).toBeDefined()
    expect(screen.getByText(/nothing is picking it up/i)).toBeDefined()
    expect(screen.queryByText('Still trying')).toBeNull()
    expect(screen.queryByText(/next attempt due now/i)).toBeNull()

    // And the button stops saying it is bringing an attempt forward, because there is no schedule
    // left to bring anything forward from.
    expect(screen.getByRole('button', { name: 'Retry' })).toBeDefined()
  })

  /**
   * **The badge counts overdue rows too (slice 7.2).**
   *
   * 6.2 counted `failed` alone, and recorded the consequence as a known blind spot: a `pending` row
   * that is due in the *past* and not being claimed — a stopped worker, or a backlog draining slower
   * than it fills — appeared on neither the list nor the badge. So the one number in the admin
   * chrome could read zero while nothing at all was reaching AXA, which is the exact moment somebody
   * is asking where a photograph went. Long-`pending` rows are still deliberately uncounted, because
   * they clear themselves and a badge that flickered with the retry schedule is one nobody trusts.
   */
  it('sums failed and overdue in the badge', async () => {
    stubFetch([FAILED], { overdue: 2 })

    show(<CountProbe />)

    await waitFor(() => {
      expect(screen.getByTestId('count').textContent).toBe('3')
    })
  })
})

function futureIso(ms: number): string {
  // No zone suffix, like the API's own timestamps — the page is expected to read it as UTC.
  return new Date(Date.now() + ms).toISOString().replace('Z', '')
}

function stubFetch(
  rows: unknown[],
  {
    retryStatus = 200,
    retryBody = '',
    overdue = 0,
  }: { retryStatus?: number; retryBody?: string; overdue?: number } = {},
) {
  vi.stubGlobal(
    'fetch',
    vi.fn((url: string, init?: RequestInit) => {
      const path = String(url)
      calls.push(`${init?.method ?? 'GET'} ${path}`)

      if (init?.method === 'POST') {
        return Promise.resolve(new Response(retryBody, { status: retryStatus }))
      }
      if (path.endsWith('/count')) {
        return Promise.resolve(
          new Response(JSON.stringify({ failed: rows.length, overdue }), { status: 200 }),
        )
      }
      return Promise.resolve(new Response(JSON.stringify(rows), { status: 200 }))
    }),
  )
}

/**
 * Stands in for the badge `AdminLayout` renders, so the count key has an observer — and it sums the
 * two numbers exactly as `App.tsx` does, because that sum is the behaviour slice 7.2 added.
 */
function CountProbe() {
  const { data } = useOutboxFailedCount()
  return <span data-testid="count">{(data?.failed ?? 0) + (data?.overdue ?? 0)}</span>
}

function show(extra?: ReactNode) {
  render(
    <TestQueryProvider>
      <MemoryRouter initialEntries={['/admin/failed-pushes']}>
        <AdminFailedPushesPage />
        {extra}
      </MemoryRouter>
    </TestQueryProvider>,
  )
}
