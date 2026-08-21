import { urlBase64ToUint8Array } from './urlBase64'

/** What the server needs in order to push to this browser (design.md §8, slice 3.4). */
export interface BrowserSubscription {
  endpoint: string
  p256dh: string
  auth: string
}

export type PushFailure = 'unsupported' | 'denied' | 'blocked' | 'failed'

export class PushError extends Error {
  readonly reason: PushFailure

  constructor(reason: PushFailure) {
    super(`Push subscription failed: ${reason}`)
    this.reason = reason
  }
}

/**
 * What the panel shows instead of a working subscription.
 *
 * Same rule as `GEOLOCATION_EXPLANATIONS`: say what to do differently, or the button just gets
 * pressed again. `blocked` and `denied` are deliberately separate — a permission refused just now is
 * recoverable by pressing again, while one refused permanently is only recoverable through browser
 * settings, and telling the second user to "press again" wastes their time at a crash site.
 */
export const PUSH_EXPLANATIONS: Record<PushFailure, string> = {
  unsupported:
    'This browser cannot receive notifications. Use the AXA app on the phone you were dispatched ' +
    'with, or keep this page open to see new claims.',
  denied:
    'Notifications were not enabled. AXA sends new claims to this device as a popup, so without ' +
    'them you will only see a claim when you open this page. Press Enable notifications again and ' +
    'choose Allow.',
  blocked:
    'Notifications are blocked for this site. Pressing the button again cannot undo that — open ' +
    'the padlock in the address bar, set Notifications to Allow, then reload this page.',
  failed:
    'Notifications could not be enabled. Check the connection and press Enable notifications again.',
}

/**
 * The whole browser surface behind one injectable function — the `requestPosition` / `startBrowserRecording`
 * idiom, and for the same reason: jsdom has no `serviceWorker`, no `PushManager` and no
 * `Notification`, so a test that stubbed all three would mostly be testing its own stubs. Injecting
 * the *subscribe* leaves a seam a fake can fill honestly, and leaves registration, permission
 * prompts and key negotiation to the manual Chrome pass, which is the only place they are real.
 */
export type SubscribeToPush = (vapidPublicKey: string) => Promise<BrowserSubscription>

/** Reads the current permission without prompting, so the panel can render the right label. */
export type ReadPushPermission = () => NotificationPermission | 'unsupported'

/** Whether this browser already holds a subscription. Silent — never prompts. */
export type ReadExistingSubscription = () => Promise<boolean>

/**
 * Is this browser already registered?
 *
 * `getRegistration()` rather than `ready`: `ready` never resolves when no worker has been registered,
 * so a browser that has never enabled notifications would hang here for ever instead of answering
 * "no" — and the panel would sit in its default state with no way to tell why.
 */
export const hasBrowserSubscription: ReadExistingSubscription = async () => {
  if (typeof navigator === 'undefined' || !navigator.serviceWorker) {
    return false
  }

  const registration = await navigator.serviceWorker.getRegistration()
  if (!registration?.pushManager) {
    return false
  }

  return (await registration.pushManager.getSubscription()) !== null
}

export const readBrowserPushPermission: ReadPushPermission = () => {
  if (typeof Notification === 'undefined' || !('serviceWorker' in navigator)) {
    return 'unsupported'
  }

  return Notification.permission
}

/**
 * The real thing. Registers the worker, asks for permission, subscribes, and flattens the browser's
 * `PushSubscription` into the three values the server stores.
 *
 * **Only ever called from a click handler.** `Notification.requestPermission()` needs a user gesture,
 * and a browser that is asked without one either refuses outright or — worse for us — counts it as a
 * dismissal against the site. Hence the explicit button in `PushPanel`, rather than subscribing on
 * mount, which is what a hook like this usually does.
 */
export const subscribeInBrowser: SubscribeToPush = async (vapidPublicKey) => {
  if (
    typeof Notification === 'undefined' ||
    typeof navigator === 'undefined' ||
    !navigator.serviceWorker
  ) {
    throw new PushError('unsupported')
  }

  // Checked before prompting: once a site is blocked, requestPermission() resolves 'denied'
  // immediately without showing anything, and the expert would see the button do nothing at all.
  if (Notification.permission === 'denied') {
    throw new PushError('blocked')
  }

  const registration = await navigator.serviceWorker.register('/sw.js')

  // `ready` rather than the register() result: a worker that is installing cannot be subscribed
  // through, and on a first visit register() resolves well before the worker is active.
  await navigator.serviceWorker.ready

  if ((await Notification.requestPermission()) !== 'granted') {
    throw new PushError('denied')
  }

  if (!registration.pushManager) {
    throw new PushError('unsupported')
  }

  let subscription: PushSubscription
  try {
    subscription = await registration.pushManager.subscribe({
      // Required by every browser that implements push: a subscription that could deliver a silent
      // push is a tracking vector, so they refuse to create one.
      userVisibleOnly: true,
      applicationServerKey: urlBase64ToUint8Array(vapidPublicKey),
    })
  } catch {
    throw new PushError('failed')
  }

  return describe(subscription)
}

/** Flattens the browser's subscription into what the API stores. Exported for the manual pass. */
export function describe(subscription: PushSubscription): BrowserSubscription {
  const json = subscription.toJSON()
  const keys = json.keys ?? {}

  if (!json.endpoint || !keys.p256dh || !keys.auth) {
    // The server refuses an incomplete subscription with a 400; failing here says why, in a sentence
    // the expert can act on, rather than surfacing as a bare API error.
    throw new PushError('failed')
  }

  return { endpoint: json.endpoint, p256dh: keys.p256dh, auth: keys.auth }
}
