import { clearTokens, getTokens, setTokens, type TokenPair } from './tokens'

export class ApiError extends Error {
  readonly status: number
  readonly body: string

  constructor(status: number, body: string) {
    super(`API error ${status}`)
    this.status = status
    this.body = body
  }
}

async function tryRefresh(): Promise<boolean> {
  const tokens = getTokens()
  if (!tokens) return false
  const response = await fetch('/auth/refresh', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ refreshToken: tokens.refreshToken }),
  })
  if (!response.ok) {
    clearTokens()
    return false
  }
  setTokens((await response.json()) as TokenPair)
  return true
}

function withBearer(init: RequestInit): RequestInit {
  const tokens = getTokens()
  // A multipart upload must NOT carry a Content-Type of ours: only the browser knows the boundary
  // it generated, and a hand-set `multipart/form-data` without one is unparseable — the media
  // endpoint answers `415 not_multipart`. Handled here rather than in a second fetch helper so the
  // upload keeps the refresh-and-retry below; a parallel path would quietly lose it, and the
  // failure would look like a random logout halfway through filling a bucket.
  const isFormData = typeof FormData !== 'undefined' && init.body instanceof FormData
  return {
    ...init,
    headers: {
      ...(isFormData ? {} : { 'Content-Type': 'application/json' }),
      ...(tokens ? { Authorization: `Bearer ${tokens.accessToken}` } : {}),
      ...init.headers,
    },
  }
}

/** JSON fetch with bearer; one refresh-and-retry on 401, then redirect to login. */
export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  let response = await fetch(path, withBearer(init))
  if (response.status === 401) {
    if (await tryRefresh()) {
      response = await fetch(path, withBearer(init))
    } else {
      window.location.assign('/login')
    }
  }
  const body = await response.text()
  if (!response.ok) throw new ApiError(response.status, body)
  return (body ? JSON.parse(body) : undefined) as T
}
