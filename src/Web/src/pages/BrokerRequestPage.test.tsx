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

/**
 * B4 (design.md §5.3) — the broker's review of what their Option 2 customer submitted.
 *
 * The first three tests are the week-5 browser pass's finding 2, turned into a guard: an Option 2
 * request in `link_issued` used to render `OutcomePanel`'s not-yet-sent branch, so a request nobody
 * had touched read "This request is filed, but the email has not gone yet" over a **Send email**
 * button the server refused with `409 not_submitted`. `emailedAt` is null in both cases and cannot
 * tell them apart; only the state can.
 */
describe('B4 — the customer\'s submission', () => {
  beforeEach(() => {
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: () => 'blob:PLACEHOLDER',
      revokeObjectURL: vi.fn(),
    })
  })

  const OPTION_2 = {
    option: 2,
    customerMobile: '+999000000123',
    linkExpiresAt: '2026-08-29T15:00:00',
  } satisfies Partial<BrokerRequestDetail>

  it.each(['link_issued', 'customer_in_progress'] as const)(
    'offers no send on a %s request, and does not call it filed',
    async (state) => {
      show(detail({ ...OPTION_2, state }), [], true)

      expect(await screen.findByText('Nothing to review yet')).toBeTruthy()

      // The two sentences that used to appear together on one card.
      expect(screen.queryByRole('button', { name: 'Send email' })).toBeNull()
      expect(screen.queryByText(/This request is filed/)).toBeNull()
    },
  )

  it('does not show what the customer has typed so far', async () => {
    // "Half a form is not information, it is a person mid-sentence" — and until they press Send there
    // is nothing on the row anyway, which is why the fields are null rather than partial.
    show(
      detail({
        ...OPTION_2,
        state: 'customer_in_progress',
        insuredName: null,
        insuranceType: null,
        insuredAddress: null,
        carValue: null,
        estimatedPremium: null,
        effectiveDate: null,
      }),
      [],
      true,
    )

    expect(await screen.findByText('Nothing to review yet')).toBeTruthy()
    expect(screen.queryByText('25000.00')).toBeNull()
  })

  it('names the desk before the send, not after it', async () => {
    show(
      detail({ ...OPTION_2, state: 'ready_to_send', submittedAt: '2026-08-24T09:00:00' }),
      [CAPTURED],
      true,
    )

    expect(await screen.findByRole('button', { name: 'Send email' })).toBeTruthy()

    // `emailRecipient` is still null here — the send writes it — so this address can only have come
    // from the routing table. That is the B4 artboard's rule: "if the routing table is wrong, this is
    // the screen where somebody notices", and noticing has to be possible *before* pressing.
    expect(await screen.findByText(RECIPIENT)).toBeTruthy()
  })

  it('sends once for three clicks in the same tick', async () => {
    show(detail({ ...OPTION_2, state: 'ready_to_send' }), [CAPTURED], true)

    const send = await screen.findByRole('button', { name: 'Send email' })

    // Three raw clicks inside one `act`: `fireEvent` flushes React between events, so by the second
    // the `disabled` attribute would swallow the rest and the test would pass with the latch deleted
    // (4.3's finding).
    await act(async () => {
      send.click()
      send.click()
      send.click()
    })

    await waitFor(() => {
      expect(postsTo('/send')).toBe(1)
    })
  })

  it('offers a new link, and no send, once the link has expired', async () => {
    show(detail({ ...OPTION_2, state: 'expired' }), [], true)

    expect(await screen.findByText('This link has run out')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Send email' })).toBeNull()
  })

  it('offers Resend when B4 sent it and the email did not go', async () => {
    // §5.3's ordering on Option 2: the transition committed, the send did not. Slice 5.3 widened
    // `Resend` to cover exactly this, because the customer's link is locked and they are gone.
    show(
      detail({ ...OPTION_2, state: 'sent', submittedAt: '2026-08-24T09:00:00', emailedAt: null }),
      [CAPTURED],
      true,
    )

    expect(await screen.findByText('Not sent yet')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Send email' })).toBeTruthy()
    expect(screen.queryByText('Sent to AXA')).toBeNull()
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
    // #14's types and #13's routing, as `/api/broker/config` serves them. B4 reads the routing to
    // name the desk **before** the send, because `emailRecipient` is written by the send itself.
    if (path.endsWith('/api/broker/config')) {
      return Promise.resolve(
        json({ insuranceTypes: ['MOTOR ALL RISK'], emailRouting: { 'MOTOR ALL RISK': RECIPIENT } }),
      )
    }
    if (path.endsWith('/submit') || path.endsWith('/resend') || path.endsWith('/send')) {
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
