const KEY = 'axa.tokens'

export interface TokenPair {
  accessToken: string
  refreshToken: string
}

export function getTokens(): TokenPair | null {
  const raw = localStorage.getItem(KEY)
  return raw ? (JSON.parse(raw) as TokenPair) : null
}

export function setTokens(pair: TokenPair): void {
  localStorage.setItem(KEY, JSON.stringify(pair))
}

export function clearTokens(): void {
  localStorage.removeItem(KEY)
}
