import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from './client'

describe('api', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn(() => Promise.resolve(new Response('{}', { status: 200 })))
    vi.stubGlobal('fetch', fetchMock)
  })

  function headersOf(): Record<string, string> {
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    return init.headers as Record<string, string>
  }

  it('sends JSON with a JSON content type', async () => {
    await api('/api/PLACEHOLDER', { method: 'POST', body: JSON.stringify({ a: 1 }) })

    expect(headersOf()['Content-Type']).toBe('application/json')
  })

  it('sends multipart without a content type of its own', async () => {
    // Only the browser knows the boundary it generated. Setting `multipart/form-data` by hand
    // produces a body the server cannot parse, and the media endpoint answers 415 not_multipart.
    const body = new FormData()
    body.append('bucket', 'insured_car_photo')

    await api('/api/PLACEHOLDER', { method: 'POST', body })

    expect(headersOf()['Content-Type']).toBeUndefined()
  })

  it('still carries the bearer token on a multipart request', async () => {
    // The upload goes through this helper precisely so it keeps the auth and the refresh-on-401
    // retry; a separate fetch path would have lost both.
    localStorage.setItem(
      'axa.tokens',
      JSON.stringify({ accessToken: 'PLACEHOLDER-access', refreshToken: 'PLACEHOLDER-refresh' }),
    )
    try {
      await api('/api/PLACEHOLDER', { method: 'POST', body: new FormData() })

      expect(headersOf().Authorization).toBe('Bearer PLACEHOLDER-access')
      expect(headersOf()['Content-Type']).toBeUndefined()
    } finally {
      localStorage.removeItem('axa.tokens')
    }
  })
})
