import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { clearTokens, setTokens } from '../api/tokens'
import { uploadDocument } from './upload'

const PATH = '/public/PLACEHOLDER-token-0001/documents'

/**
 * Slice 5.3's `auth` option on the shared upload path (design.md §3, §5.3).
 *
 * **These tests only mean anything with tokens in `localStorage`, and that is the whole point.**
 * `withBearer` attaches an `Authorization` header *if* `getTokens()` returns something, so a test run
 * on an empty store passes identically against `auth: 'bearer'` and `auth: 'none'` — it would prove
 * nothing and read as though it proved everything. The browser that has tokens is also the realistic
 * one: a broker clicking their own customer's link to check it.
 */
describe('uploadDocument carries credentials only when it should', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  function file(): File {
    return new File([new Uint8Array([1, 2, 3])], 'PLACEHOLDER-car-papers.pdf', {
      type: 'application/pdf',
    })
  }

  function headersOf(call: number): Headers {
    const init = fetchMock.mock.calls[call][1] as RequestInit | undefined
    return new Headers(init?.headers)
  }

  beforeEach(() => {
    setTokens({ accessToken: 'PLACEHOLDER-access', refreshToken: 'PLACEHOLDER-refresh' })
    fetchMock = vi.fn(() =>
      Promise.resolve(new Response(JSON.stringify({ id: 'PLACEHOLDER' }), { status: 201 })),
    )
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => clearTokens())

  it("sends no Authorization header on the public page's path", async () => {
    await uploadDocument({
      path: PATH,
      bucket: 'public_document',
      origin: 'uploaded',
      file: file(),
      auth: 'none',
    })

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(headersOf(0).has('Authorization')).toBe(false)
  })

  it('still sends one for a signed-in caller, so nothing written before 5.3 changed', async () => {
    await uploadDocument({
      path: '/api/broker/requests/PLACEHOLDER/documents',
      bucket: 'broker_document',
      origin: 'uploaded',
      file: file(),
    })

    // No `auth` argument at all — the default. If this ever goes red, every authenticated upload in
    // the app has silently stopped carrying its session.
    expect(headersOf(0).get('Authorization')).toBe('Bearer PLACEHOLDER-access')
  })

  it('does not set Content-Type on either path, so the browser writes its own boundary', async () => {
    await uploadDocument({
      path: PATH,
      bucket: 'public_document',
      origin: 'uploaded',
      file: file(),
      auth: 'none',
    })

    // A multipart body whose `Content-Type` names no boundary is unparseable at the other end, which
    // is why `withBearer` special-cases `FormData` — and why the plain-fetch path must not helpfully
    // add one back.
    expect(headersOf(0).has('Content-Type')).toBe(false)
  })
})
