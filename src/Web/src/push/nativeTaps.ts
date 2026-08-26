/**
 * Deep-linking a tapped native notification (slice 6.3).
 *
 * **The contract is `data.url`, and it is deliberately the same field `public/sw.js` reads** on
 * `notificationclick`. `PushMessage` in `IPushSender.cs` carries `{title, body, url}`; the FCM
 * sender puts `url` into `message.data.url` and the service worker puts it into
 * `notification.data.url`, so one payload shape deep-links on both channels and §8's four
 * still-to-come push events need no second contract. A notification you have to go and find the
 * subject of is most of the way to no notification.
 *
 * Same seam idiom as the rest: the Capacitor package is behind a dynamic import, so jsdom never
 * loads it and a browser build leaves it in an async chunk.
 */

/** Where a tap should land. `navigate` from react-router in the app; a spy in tests. */
export type NavigateTo = (url: string) => void

/**
 * Starts listening for notification taps. Returns a function that stops listening.
 *
 * **Foreground-received notifications are deliberately not re-displayed in-app.** Android suppresses
 * its own tray notification while the app is in the foreground, and the obvious next step — showing
 * a toast or a modal — is inventing UI the BRD does not describe, on top of a screen the expert is
 * already looking at. The smaller interpretation is recorded in `scope-decisions.md`: a claim that
 * arrives while the app is open appears in the list, which is where the expert already is. Only
 * `pushNotificationActionPerformed` is wired, which fires when a notification is actually tapped.
 */
export async function wireNativeNotificationTaps(navigate: NavigateTo): Promise<() => void> {
  const { PushNotifications } = await import('@capacitor/push-notifications')

  const listener = await PushNotifications.addListener(
    'pushNotificationActionPerformed',
    (action) => {
      const data = action.notification.data as { url?: unknown } | undefined
      const url = typeof data?.url === 'string' && data.url.length > 0 ? data.url : '/'

      // Falls back to the app root rather than doing nothing. A tap that appears to do nothing reads
      // as a broken app; landing on the worklist is always somewhere useful, and it is what `sw.js`
      // already does with `payload.url || '/'`.
      navigate(url)
    },
  )

  return () => {
    void listener.remove()
  }
}
