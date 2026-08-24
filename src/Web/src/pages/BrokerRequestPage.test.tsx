import { act, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { BrokerDocument, BrokerRequestDetail } from '../broker/api'
import type { MediaConfig } from '../media/config'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import BrokerRequestPage from './BrokerRequestPage'

const ID = '00000000-0000-0000-0000-0000000b0001'
const RECIPIENT = 'PLACEHOLDER-recipient-1@example.invalid'

/**
 * §7.1's broker row as `GET /api/config/media` serves it. **`allowUpload` here is the *effective*
 * value** — the server applies `Broker:AllowUpload` over the table before projecting it — which is
 * what lets the kill-switch remove B2's file control without a release.
 */
function mediaConfig(allowUpload: boolean): MediaConfig {
  return {
    clarity: { minWidth: 1024, minHeight: 768, blurVarianceThreshold: 100, blurAnalysisMaxEdge: 512 },
    maxFileMb: 15,
    buckets: [
      { bucket: 'broker_document', allowUpload, contentTypes: ['image/jpeg', 'application/pdf'] },
    ],
  }
}

function detail(overrides: Partial<BrokerRequestDetail> = {}): BrokerRequestDetail {
  return {
    id: ID,
    option: 1,
    state: 'draft',
    insuredName: 'PLACEHOLDER Insured Three',
    insuranceType: 'MOTOR ALL RISK',
    insuredAddress: 'PLACEHOLDER Address 1',
    carValue: 25000,
    estimatedPremium: 1200,
    effectiveDate: '2026-09-01',
    customerMobile: null,
    createdAt: '2026-08-22T15:02:00',
    submittedAt: null,
    emailedAt: null,
    emailRecipient: null,
    linkExpiresAt: null,
    ...overrides,
  }
}

const CAPTURED: BrokerDocument = {
  id: '00000000-0000-0000-0000-0000000bd001',
  bucket: 'broker_document',
  // Null, and that is the point of `PushTiming.Never`: NEXT3 never sees this file, so demanding a
  // #12 code for it would be inventing client data.
  docType: null,
  origin: 'captured',
  clarityResult: 'passed',
  contentType: 'image/jpeg',
  fileName: 'id-card.jpg',
  sizeBytes: 2048,
  pushStatus: 'n/a',
  pushConfirmed: false,
  blobRetained: true,
  createdAt: '2026-08-22T15:03:00',
}

const UPLOADED: BrokerDocument = {
  ...CAPTURED,
  id: '00000000-0000-0000-0000-0000000bd002',
  origin: 'uploaded',
  contentType: 'application/pdf',
  fileName: 'car-papers.pdf',
}

let fetchMock: ReturnType<typeof vi.fn>

describe('B2 — one request', () => {
  beforeEach(() => {
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: () => 'blob:PLACEHOLDER',
      revokeObjectURL: vi.fn(),
    })
  })

  it('offers both controls on a draft while the kill-switch is on', async () => {
    show(detail(), [], true)

    expect(await screen.findByLabelText(/^Take a photo/)).toBeTruthy()
    expect(screen.getByLabelText(/^Choose a file/)).toBeTruthy()
  })

  it('removes the file control entirely when the kill-switch is off', async () => {
    show(detail(), [], false)

    // **Gone, not disabled.** "A disabled picker is a picker somebody finds a way to use, and the
    // whole point of the switch is that AXA can insist on photographs taken here."
    expect(await screen.findByLabelText(/^Take a photo/)).toBeTruthy()
    expect(screen.queryByLabelText(/^Choose a file/)).toBeNull()
  })

  it('tags every document with how it arrived', async () => {
    show(detail(), [CAPTURED, UPLOADED], true)

    // "AXA asked for that flag and it is on every row in the system, not only here."
    expect(await screen.findByText('captured')).toBeTruthy()
    expect(screen.getByText('uploaded')).toBeTruthy()
  })

  it('submits once for three clicks in the same tick', async () => {
    show(detail(), [CAPTURED], true)

    const submit = await screen.findByRole('button', { name: 'Submit' })

    await act(async () => {
      submit.click()
      submit.click()
      submit.click()
    })

    await waitFor(() => {
      expect(postsTo('/submit')).toBe(1)
    })
  })

  it('names the recipient address on the sent state rather than saying "sent successfully"', async () => {
    show(
      detail({
        state: 'submitted',
        submittedAt: '2026-08-22T15:05:00',
        emailedAt: '2026-08-22T15:05:00',
        emailRecipient: RECIPIENT,
      }),
      [CAPTURED],
      true,
    )

    // "If the routing table is wrong, this is the screen where somebody notices."
    expect(await screen.findByText('Sent to AXA')).toBeTruthy()
    expect(screen.getByText(RECIPIENT)).toBeTruthy()

    // And no way to attach anything more — the email has already been built.
    expect(screen.queryByLabelText(/^Take a photo/)).toBeNull()
  })

  it('says so, and offers the send, when the request is filed but the email did not go', async () => {
    show(
      detail({ state: 'submitted', submittedAt: '2026-08-22T15:05:00', emailedAt: null }),
      [CAPTURED],
      true,
    )

    // The state §5.3's ordering exists to make visible: filed, delivery still owed. Reporting this
    // as "Sent to AXA" is the lie the ordering was chosen to avoid.
    expect(await screen.findByText('Not sent yet')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Send email' })).toBeTruthy()
    expect(screen.queryByText('Sent to AXA')).toBeNull()
  })

  it('shows the amounts with no currency symbol', async () => {
    show(detail(), [], true)

    const value = await screen.findByText('25000.00')
    expect(value.textContent).toBe('25000.00')
    expect(screen.queryByText(/AED|USD|\$/)).toBeNull()
  })
})

function postsTo(suffix: string): number {
  return fetchMock.mock.calls.filter(([url, init]) => {
    const request = init as RequestInit | undefined
    return String(url).endsWith(suffix) && request?.method === 'POST'
  }).length
}

function show(body: BrokerRequestDetail, documents: BrokerDocument[], allowUpload: boolean) {
  fetchMock = vi.fn((url: string) => {
    const path = String(url)
    if (path.endsWith('/api/config/media')) return Promise.resolve(json(mediaConfig(allowUpload)))
    if (path.endsWith('/submit') || path.endsWith('/resend')) {
      return Promise.resolve(json({ state: 'submitted', emailFailed: false, recipient: RECIPIENT }))
    }
    if (path.endsWith('/documents')) return Promise.resolve(json(documents))
    return Promise.resolve(json(body))
  })
  vi.stubGlobal('fetch', fetchMock)

  render(
    <TestQueryProvider>
      <MemoryRouter initialEntries={[`/broker/${ID}`]}>
        <Routes>
          <Route path="/broker/:id" element={<BrokerRequestPage />} />
        </Routes>
      </MemoryRouter>
    </TestQueryProvider>,
  )
}

function json(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200 })
}
