/*
 * The service worker that turns a web push into the BRD's "popup message on the expert mobile"
 * (design.md §8, slice 3.4).
 *
 * Deliberately a plain file in `public/` rather than a generated one: `vite-plugin-pwa` brings
 * Workbox, a manifest and a precache manifest, and this application needs none of them — it needs
 * two event handlers. Vite copies `public/` verbatim, so this is served from the origin root, which
 * is the scope a push subscription needs.
 *
 * ESLint does check this file (its flat config picks up plain .js), but TypeScript does not:
 * `tsconfig.app.json` includes only `src`. So `self` and the service-worker globals are untyped here
 * and there is no compile-time contract with the server's payload — which is why the shape is stated
 * just below, and why the manual Chrome pass is the DoD. A service worker can only really be proven
 * in a browser anyway.
 *
 * The payload shape is a contract with `PushMessage` on the server (`IPushSender.cs`): {title, body, url}.
 */

// Take over as soon as this version installs, rather than waiting for every tab to close. A worker
// that is one deploy behind delivers notifications with last week's click-through behaviour, and
// nobody thinks to close every tab to fix a notification.
self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()))

self.addEventListener('push', (event) => {
  // A push with no data, or with data that is not our JSON, still has to show *something*: the
  // expert has been told there is a claim, and a silent drop is the one outcome worse than a vague
  // notification. Push services also send empty pushes to keep a subscription alive.
  let payload
  try {
    payload = event.data ? event.data.json() : {}
  } catch {
    payload = {}
  }

  const title = payload.title || 'AXA Motor Claims'
  const options = {
    body: payload.body || 'You have a new notification.',
    // The URL travels in `data` so `notificationclick` can read it back — a notification is a
    // separate object from the push that created it, and nothing else survives the gap.
    data: { url: payload.url || '/' },
    icon: '/favicon.svg',
    badge: '/favicon.svg',
    // Assignments are the thing this app exists to deliver: it should still be on screen when the
    // expert picks the phone up, not gone because they were driving.
    requireInteraction: true,
    // One notification per claim rather than a stack of identical ones if a push is retried.
    tag: payload.url || 'axa-motor-claims',
  }

  event.waitUntil(self.registration.showNotification(title, options))
})

self.addEventListener('notificationclick', (event) => {
  event.notification.close()

  const url = (event.notification.data && event.notification.data.url) || '/'

  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((windows) => {
      // Focus a tab that is already open rather than piling up new ones — an expert working a claim
      // and then tapping the notification for that same claim should land back where they were.
      for (const client of windows) {
        if ('focus' in client) {
          // navigate() first, because the open tab is probably on E1 or on a different claim.
          if ('navigate' in client) {
            return client.navigate(url).then((navigated) => (navigated || client).focus())
          }
          return client.focus()
        }
      }

      return self.clients.openWindow(url)
    }),
  )
})

/*
 * The push service rotated this browser's subscription (slice 7.2).
 *
 * Browsers fire this when they invalidate a subscription and issue a new one — key rotation, a long
 * idle period, storage pressure. Without a handler the old endpoint simply dies: every push to it
 * comes back 410, the server revokes the row, and the expert's popups stop with nothing on screen to
 * say so. Re-subscribing with the *old* options is what keeps the browser holding one at all.
 *
 * **Deliberate deviation, recorded rather than worked around: this cannot tell the server.** A
 * service worker holds no bearer token — `api()`'s session lives in the page, not here — and minting
 * one for a worker would put a long-lived credential somewhere §9 has no way to revoke. So the new
 * subscription sits in the browser until the next time the app is opened, when
 * `usePushSubscription`'s mount effect re-posts it. That is the same mechanism that fixes the
 * "Notifications are on" lie, doing double duty; the gap is one app open wide, and the alternative
 * is worse.
 *
 * `oldSubscription.options` rather than a fresh key fetch, for the same reason: fetching the VAPID
 * key needs no auth today but does need the origin to be up, and a rotation that happens while the
 * network is unavailable should still leave the browser subscribed to the key it was using.
 */
self.addEventListener('pushsubscriptionchange', (event) => {
  const options = event.oldSubscription && event.oldSubscription.options

  if (!options) {
    return
  }

  event.waitUntil(
    self.registration.pushManager.subscribe({
      userVisibleOnly: true,
      applicationServerKey: options.applicationServerKey,
    }),
  )
})
