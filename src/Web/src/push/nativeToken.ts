/**
 * Acquiring an FCM registration token inside the Android shell (slice 6.3).
 *
 * **The Capacitor WebView exposes no `PushManager` at all** — confirmed on the Samsung in 6.3a, and
 * the reason this slice exists. `readBrowserPushPermission()` therefore returns `'unsupported'`
 * inside the shell and `PushPanel` renders "this browser cannot show claim notifications", which is
 * exactly right for a browser and exactly wrong for the native app. Unlike iOS, Android has no
 * web-push fallback to degrade to, so FCM through the native layer is the only path.
 *
 * Same seam shape as `subscription.ts`: an injectable function type with a real default, and the
 * Capacitor package behind a **dynamic** import so jsdom never loads it and a browser build keeps it
 * in an async chunk nothing fetches.
 */
import { PushError } from './subscription'

/** Asks the handset for a push token. Rejects with a {@link PushError} the panel can explain. */
export type AcquirePushToken = () => Promise<string>

/**
 * Permission, registration, and the token FCM answers with.
 *
 * **The token arrives on an event, not as a return value**, which is why this is a promise around
 * two listeners rather than an await. `register()` resolves as soon as the request is *made*; the
 * token comes back later on `'registration'`, or an error on `'registrationError'` — a device with
 * no Play Services (every Huawei handset launched since 2020, see `docs/oem-push-guidance.md`) takes
 * the second path, and treating `register()`'s resolution as success would have reported those as
 * enabled while they silently received nothing.
 */
export const acquireNativePushToken: AcquirePushToken = async () => {
  const { PushNotifications } = await import('@capacitor/push-notifications')

  let permission = await PushNotifications.checkPermissions()

  if (permission.receive === 'prompt' || permission.receive === 'prompt-with-rationale') {
    permission = await PushNotifications.requestPermissions()
  }

  if (permission.receive !== 'granted') {
    // Two different refusals, mapped to the two explanations the panel already has: `denied` is
    // recoverable by pressing the button again and choosing Allow, `blocked` is not and sends the
    // person to system settings. On Android 13+ this is the POST_NOTIFICATIONS prompt.
    throw new PushError(permission.receive === 'denied' ? 'blocked' : 'denied')
  }

  return new Promise<string>((resolve, reject) => {
    // Registered before `register()` is called: the token can arrive on the very next tick, and a
    // listener attached afterwards would miss it and hang until the caller gave up.
    const listeners = [
      PushNotifications.addListener('registration', (token) => {
        void detach()
        resolve(token.value)
      }),
      PushNotifications.addListener('registrationError', () => {
        void detach()
        // Deliberately not the platform's message: it names Google Play Services internals, and the
        // panel's `failed` copy is what an expert at a crash site actually reads.
        reject(new PushError('unsupported'))
      }),
    ]

    async function detach() {
      // Both removed whichever way this settled, so pressing the button twice in one session does
      // not accumulate handlers that resolve an already-settled promise.
      for (const listener of listeners) {
        await (await listener).remove()
      }
    }

    void PushNotifications.register().catch((error: unknown) => {
      void detach()
      reject(error instanceof Error ? error : new PushError('failed'))
    })
  })
}
