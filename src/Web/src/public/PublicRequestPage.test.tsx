import { act, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { MediaConfig } from '../media/config'
import { setTokens } from '../api/tokens'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import PublicRequestPage from './PublicRequestPage'
import type { PublicDocument, PublicLinkView } from './api'

const TOKEN = 'PLACEHOLDER-token-0001'
const BROKER = 'PLACEHOLDER Broker One'
const MOBILE = '+999000000123'

const MEDIA: MediaConfig = {
  clarity: { minWidth: 1024, minHeight: 768, blurVarianceThreshold: 100, blurAnalysisMaxEdge: 512 },
  maxFileMb: 15,
  buckets: [
    { bucket: 'public_document', allowUpload: true, contentTypes: ['image/jpeg', 'application/pdf'] },
  ],
}

function view(overrides: Partial<PublicLinkView> = {}): PublicLinkView {
  return {
    state: 'customer_in_progress',
    expiresAt: '2026-08-31T12:00:00',
    maxFiles: 15,
    maxFileMb: 10,
    brokerDisplayName: BROKER,
    insuranceTypes: ['MOTOR ALL RISK', 'MOTOR TOTAL LOSS', 'PLACEHOLDER-TYPE-3'],
    ...overrides,
  }
}

const DOCUMENT: PublicDocument = {
  id: '00000000-0000-0000-0000-0000000pd001'.replace(/p/g, 'a'),
  bucket: 'public_document',
  fileName: 'car-papers.pdf',
  sizeBytes: 4096,
}

let fetchMock: ReturnType<typeof vi.fn>

describe('P1 — the public customer form', () => {
  beforeEach(() => {
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: () => 'blob:PLACEHOLDER',
      revokeObjectURL: vi.fn(),
    })
    localStorage.clear()
  })

  it('names the broker who asked for it', async () => {
    show(view(), [])

    // §9.1's one identifying value, and it is here on purpose: an anonymous page asking a member of
    // the public for identity documents is the shape of a phishing page (pass-2 review decision 3).
    expect(await screen.findByText(new RegExp(BROKER))).toBeTruthy()
  })

  it('shows no name at all when the link predates the snapshot column', async () => {
    // Every link issued before slice 5.2 has `broker_display_name` null. A placeholder here would
    // read as a bug on the one screen that has to look trustworthy.
    show(view({ brokerDisplayName: null }), [])

    expect(await screen.findByText(/You have been asked to complete this/)).toBeTruthy()
    expect(screen.queryByText(new RegExp(BROKER))).toBeNull()
  })

  it('offers only the insurance types the server will accept', async () => {
    show(view(), [])

    const select = (await screen.findByLabelText('Insurance type')) as HTMLSelectElement
    const offered = [...select.options].map((option) => option.value).filter(Boolean)

    // #14's list, served with the link. A list written into TypeScript would be a placeholder
    // violation *and* a second source of truth from the one the submit validates against.
    expect(offered).toEqual(['MOTOR ALL RISK', 'MOTOR TOTAL LOSS', 'PLACEHOLDER-TYPE-3'])
  })

  it('will not continue until all six details are filled in', async () => {
    show(view(), [])

    const proceed = await screen.findByRole('button', { name: 'Continue to documents' })
    expect((proceed as HTMLButtonElement).disabled).toBe(true)

    await fill()
    await waitFor(() => {
      expect((proceed as HTMLButtonElement).disabled).toBe(false)
    })
  })

  it('will not send until a supporting document is attached', async () => {
    show(view(), [])
    await fill()
    await userEvent.click(screen.getByRole('button', { name: 'Continue to documents' }))

    const send = await screen.findByRole('button', { name: 'Send to AXA' })
    expect((send as HTMLButtonElement).disabled).toBe(true)
    expect(screen.getByText(/Attach at least one supporting document/)).toBeTruthy()
  })

  it('sends once for three clicks in the same tick', async () => {
    show(view(), [DOCUMENT])
    await fill()
    await userEvent.click(screen.getByRole('button', { name: 'Continue to documents' }))

    const send = await screen.findByRole('button', { name: 'Send to AXA' })

    // Three raw clicks in one `act`, not `fireEvent`: React flushes between fired events, so by the
    // second the `disabled` attribute swallows the rest and the test passes with the latch deleted
    // (4.3's finding). Here the loser would be answered with §9.1's uniform 404, which on this screen
    // reads as "your form was lost".
    await act(async () => {
      send.click()
      send.click()
      send.click()
    })

    await waitFor(() => {
      expect(postsTo('/submit')).toBe(1)
    })
  })

  /**
   * **The bug the browser pass found, eighth slice running.** The success screen counted the
   * documents query, and the submit had just locked the token — so the next fetch was §9.1's uniform
   * 404, the list was empty, and a customer who had watched two files upload was told AXA had **0
   * documents**. Nothing threw. Every other test passed, because none of them had read this screen.
   *
   * The count is captured at the moment of sending now. This test pins the number, not the screen:
   * with the old code it reads 0 and goes red.
   */
  it('counts the documents that were sent, after the link has closed behind them', async () => {
    show(view(), [DOCUMENT, { ...DOCUMENT, id: 'second', fileName: 'id-card.jpg' }])
    await fill()
    await userEvent.click(screen.getByRole('button', { name: 'Continue to documents' }))

    const send = await screen.findByRole('button', { name: 'Send to AXA' })
    await act(async () => {
      send.click()
    })

    expect(await screen.findByText('Sent to your broker')).toBeTruthy()
    expect(screen.getByText(/2 documents/)).toBeTruthy()
    expect(screen.queryByText(/0 documents/)).toBeNull()
  })

  it('does not ask the dead link for its documents again', async () => {
    show(view(), [DOCUMENT])
    await fill()
    await userEvent.click(screen.getByRole('button', { name: 'Continue to documents' }))

    const send = await screen.findByRole('button', { name: 'Send to AXA' })
    await act(async () => {
      send.click()
    })
    await screen.findByText('Sent to your broker')

    const before = getsTo('/documents')
    await act(async () => {
      await Promise.resolve()
    })

    // The token is locked, so the only answer left is the uniform 404. Asking for it would be a
    // request whose result the screen must ignore — and the version that did not ignore it is what
    // reported 0.
    expect(getsTo('/documents')).toBe(before)
  })

  /**
   * The second thing the browser pass measured. `--control: 48px` comes from `app-shell--touch`, and
   * `shellFor` assigns that class **by role** — this page has no role, so it silently inherited the
   * 36 px desk size on the most phone-only screen in the product. Asserted on the class rather than on
   * a computed height, because jsdom applies no stylesheet: what a test here can honestly check is
   * that the page asks for the touch shell, and the browser pass is what checked the pixels.
   */
  it('asks for the touch shell, because its only reader is on a phone', async () => {
    show(view(), [])
    await screen.findByLabelText('Insured name')

    const shell = document.querySelector('.app-shell')
    expect(shell?.className).toContain('app-shell--touch')
  })

  it('says the photographs are still to come rather than pretending to collect them', async () => {
    show(view(), [DOCUMENT])
    await fill()
    await userEvent.click(screen.getByRole('button', { name: 'Continue to documents' }))

    // The five car sides are slice 6.1. A working-looking control that collected nothing is the
    // version somebody would demo.
    expect(await screen.findByText(/Photographs come next/)).toBeTruthy()
  })

  it('shows the closed page for a dead link, and nothing identifying on it', async () => {
    fetchMock = vi.fn(() => Promise.resolve(new Response('', { status: 404 })))
    vi.stubGlobal('fetch', fetchMock)
    mount()

    // §9.1: invalid, expired and already-used are one uniform 404, and this screen is its one
    // rendering — no broker name, no dates, no retry, no support number. "A link that has closed must
    // not confirm that it ever existed or who it belonged to."
    expect(await screen.findByText('This link is no longer open')).toBeTruthy()
    expect(screen.queryByText(new RegExp(BROKER))).toBeNull()
    expect(screen.queryByText(new RegExp(MOBILE.replace('+', '\\+')))).toBeNull()
    expect(screen.queryByRole('button')).toBeNull()
  })

  /**
   * The reason `UploadRequest.auth` exists, asserted the only way that discriminates: **with tokens
   * in `localStorage`**. Without them `api()` would omit the header anyway and the test would pass
   * against the very code it is meant to forbid — and a broker opening their own customer's link is
   * exactly the browser that has them.
   */
  it('sends no Authorization header even when this browser holds a session', async () => {
    setTokens({ accessToken: 'PLACEHOLDER-access', refreshToken: 'PLACEHOLDER-refresh' })
    show(view(), [DOCUMENT])
    await fill()
    await userEvent.click(screen.getByRole('button', { name: 'Continue to documents' }))

    const send = await screen.findByRole('button', { name: 'Send to AXA' })
    await act(async () => {
      send.click()
    })

    await waitFor(() => {
      expect(postsTo('/submit')).toBe(1)
    })

    for (const [url, init] of fetchMock.mock.calls) {
      if (!String(url).startsWith('/public/')) continue
      const headers = new Headers((init as RequestInit | undefined)?.headers)
      expect(headers.has('Authorization'), `${String(url)} carried a bearer`).toBe(false)
    }
  })
})

async function fill() {
  await userEvent.type(await screen.findByLabelText('Insured name'), 'PLACEHOLDER Insured')
  await userEvent.selectOptions(screen.getByLabelText('Insurance type'), 'MOTOR ALL RISK')
  await userEvent.type(screen.getByLabelText('Address'), 'PLACEHOLDER Address 1')
  await userEvent.type(screen.getByLabelText('Car value'), '25000')
  await userEvent.type(screen.getByLabelText('Estimated premium'), '750')
  await userEvent.type(screen.getByLabelText('Effective date'), '2026-09-01')
}

function getsTo(suffix: string): number {
  return fetchMock.mock.calls.filter(([url, init]) => {
    const request = init as RequestInit | undefined
    return String(url).endsWith(suffix) && (request?.method ?? 'GET') === 'GET'
  }).length
}

function postsTo(suffix: string): number {
  return fetchMock.mock.calls.filter(([url, init]) => {
    const request = init as RequestInit | undefined
    return String(url).endsWith(suffix) && request?.method === 'POST'
  }).length
}

/**
 * **The stub models the token dying, and that is not decoration.** A successful submit locks the link
 * (§9.1), so from that instant every `/public/{token}/*` route answers the uniform 404 — including
 * the document list. A stub that kept serving documents afterwards is a stub in which the success
 * screen's count can never be wrong, and the first version of this file was exactly that: it passed
 * against the code a real browser caught reporting "0 documents". A test whose fake is kinder than
 * the server is a test that cannot see the bug the server causes.
 */
function show(body: PublicLinkView, documents: PublicDocument[]) {
  let locked = false

  fetchMock = vi.fn((url: string, init?: RequestInit) => {
    const path = String(url)
    if (path.endsWith('/api/config/media')) return Promise.resolve(json(MEDIA))

    if (path.endsWith('/submit')) {
      locked = true
      return Promise.resolve(new Response('', { status: 200 }))
    }

    if (locked && path.startsWith('/public/')) {
      return Promise.resolve(new Response('', { status: 404 }))
    }

    if (path.endsWith('/documents')) {
      return init?.method === 'POST'
        ? Promise.resolve(json(documents[0]))
        : Promise.resolve(json(documents))
    }

    return Promise.resolve(json(body))
  })
  vi.stubGlobal('fetch', fetchMock)
  mount()
}

function mount() {
  render(
    <TestQueryProvider>
      <MemoryRouter initialEntries={[`/p/${TOKEN}`]}>
        <Routes>
          <Route path="/p/:token" element={<PublicRequestPage />} />
        </Routes>
      </MemoryRouter>
    </TestQueryProvider>,
  )
}

function json(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200 })
}
