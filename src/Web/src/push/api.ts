import { api } from '../api/client'
import type { BrowserSubscription } from './subscription'

interface VapidPublicKey {
  publicKey: string
}

/** The application server key the browser subscribes with (design.md §8). Public by definition. */
export async function getVapidPublicKey(signal?: AbortSignal): Promise<string> {
  const { publicKey } = await api<VapidPublicKey>('/api/push/vapid-public-key', { signal })
  return publicKey
}

/** Registers this browser for the signed-in user. Idempotent server-side, by unique index. */
export function postSubscription(subscription: BrowserSubscription): Promise<{ id: string }> {
  return api<{ id: string }>('/api/push/subscriptions', {
    method: 'POST',
    body: JSON.stringify(subscription),
  })
}

/** Stops notifications for this browser. 204, so `void` — `api()` returns undefined on an empty body. */
export function deleteSubscription(endpoint: string): Promise<void> {
  return api<void>('/api/push/subscriptions', {
    method: 'DELETE',
    body: JSON.stringify({ endpoint }),
  })
}
