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

/**
 * Registers this handset's FCM token for the signed-in user (slice 6.3).
 *
 * Its own route rather than `postSubscription`, because a registration token is not a web-push
 * subscription: no https endpoint, no `p256dh`, no `auth`. Idempotent server-side by unique index,
 * the same way, and registering also takes the handset from whoever held it before — a field phone
 * is handed over, and both rows live would mean one person's claim popups on another's screen.
 */
export function registerDeviceToken(
  token: string,
  platform: 'android',
): Promise<{ id: string }> {
  return api<{ id: string }>('/api/push/device-tokens', {
    method: 'POST',
    body: JSON.stringify({ token, platform }),
  })
}

/** Stops notifications for this handset. 204, so `void`, exactly as the browser route is. */
export function deleteDeviceToken(token: string): Promise<void> {
  return api<void>('/api/push/device-tokens', {
    method: 'DELETE',
    body: JSON.stringify({ token }),
  })
}

/** Stops notifications for this browser. 204, so `void` — `api()` returns undefined on an empty body. */
export function deleteSubscription(endpoint: string): Promise<void> {
  return api<void>('/api/push/subscriptions', {
    method: 'DELETE',
    body: JSON.stringify({ endpoint }),
  })
}
