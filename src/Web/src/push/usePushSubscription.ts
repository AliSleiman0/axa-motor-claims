import { useEffect, useRef, useState } from 'react'
import { detectNativeShell } from '../media/nativeShell'
import { getVapidPublicKey, postSubscription, registerDeviceToken } from './api'
import { acquireNativePushToken, type AcquirePushToken } from './nativeToken'
import {
  hasBrowserSubscription,
  PUSH_EXPLANATIONS,
  PushError,
  readBrowserPushPermission,
  subscribeInBrowser,
  type PushFailure,
  type ReadExistingSubscription,
  type ReadPushPermission,
  type SubscribeToPush,
} from './subscription'

export interface UsePushSubscriptionOptions {
  /** The browser seam. A parameter so tests hand in a fake; production takes the default. */
  subscribe?: SubscribeToPush
  /** Reads the current permission without prompting. Injectable for the same reason. */
  readPermission?: ReadPushPermission
  /** Reads whether this browser is already subscribed. Silent — never prompts. */
  readSubscription?: ReadExistingSubscription
  /**
   * Whether the app is running inside the Android shell (slice 6.3). Injectable for the same reason
   * everything else here is; production probes for the Capacitor bridge.
   */
  shellIsNative?: boolean
  /** The native seam. Only ever called when {@link shellIsNative} is true. */
  acquireToken?: AcquirePushToken
}

export interface UsePushSubscriptionResult {
  /** Enables notifications. Only ever called from a click — permission needs a user gesture. */
  enable: () => void
  pending: boolean
  enabled: boolean
  /** True when the browser cannot do push at all, so the panel can say so instead of offering a button. */
  unsupported: boolean
  /** The explanation to render when something stopped the subscription. */
  failed: string | null
}

/**
 * Registering this browser for §8's assignment popup.
 *
 * **Nothing that could prompt happens on mount, deliberately.**
 * `Notification.requestPermission()` requires a user gesture: asked without one, a browser either
 * refuses or counts it against the origin — Chrome auto-blocked this site after a single dismissal
 * during slice 3.4's browser pass, and the block survives reloads. So there is an explicit button,
 * and nothing subscribes until it is pressed. It is also the honest UX: an expert should be told what
 * is about to pop up before it does.
 *
 * Reading the *existing* subscription on mount is a different thing and is safe, because it prompts
 * for nothing — and it is necessary. Without it the panel offered "Enable notifications" on every
 * page load to an expert who had already enabled them, which is a screen telling somebody their
 * notifications are off while their notifications are on. Found in the browser pass; no test could
 * see it, because every test mounted the hook with nothing already subscribed.
 */
export function usePushSubscription({
  subscribe = subscribeInBrowser,
  readPermission = readBrowserPushPermission,
  readSubscription = hasBrowserSubscription,
  shellIsNative = detectNativeShell() !== null,
  acquireToken = acquireNativePushToken,
}: UsePushSubscriptionOptions = {}): UsePushSubscriptionResult {
  const [permission] = useState(() => readPermission())
  const [enabled, setEnabled] = useState(false)
  const [pending, setPending] = useState(false)
  const [failed, setFailed] = useState<string | null>(null)

  // A latch, not `pending`: two clicks in the same tick both read `pending` as false because React
  // has not re-rendered, and the second one would fire a second permission prompt and a second POST.
  // 1.5's lesson, fifth slice running — verified by removing it.
  const inFlight = useRef(false)

  // **The native shell is never "unsupported", however loudly the browser API says so.** The
  // Capacitor WebView exposes no PushManager, so `readPermission()` answers 'unsupported' there and
  // the panel would render "this browser cannot show claim notifications" — inside the native app,
  // whose whole reason for existing is that it *can*. FCM is checked at the point of pressing the
  // button instead, which is also where a handset with no Play Services (Huawei) is discovered.
  const unsupported = !shellIsNative && permission === 'unsupported'

  // Only when permission is already granted: a subscription cannot exist without it, and asking the
  // service worker anything on a browser that has never been asked is pointless work on first paint.
  useEffect(() => {
    // Skipped entirely in the native shell: there is no service worker to ask, and the honest answer
    // for a handset is "we do not know". So the shell always offers the button and pressing it
    // re-registers — which is an upsert server-side and therefore free. Deliberately *not* inferred
    // from the OS notification permission being granted: that would say "Notifications are on" while
    // the server held no row, which is a defect already on 7.2's list for the browser and is not
    // worth reproducing here.
    if (shellIsNative) return undefined
    if (permission !== 'granted') return undefined

    let cancelled = false
    void readSubscription()
      .then((already) => {
        if (already && !cancelled) setEnabled(true)
      })
      // Swallowed on purpose: this only decides which of two labels to show, and a browser that
      // cannot answer should offer the button rather than an error the expert cannot act on.
      .catch(() => undefined)

    return () => {
      cancelled = true
    }
  }, [permission, readSubscription, shellIsNative])

  function enable() {
    if (enabled || unsupported || inFlight.current) return

    inFlight.current = true
    setPending(true)
    setFailed(null)

    void (async () => {
      try {
        if (shellIsNative) {
          // The native path touches no service worker and no VAPID key: FCM identifies the app by
          // the `google-services.json` compiled into it, and the server signs with a service
          // account. Nothing about web push is fetched, which is why a native failure can never
          // present as a service-worker error.
          await registerDeviceToken(await acquireToken(), 'android')
        } else {
          // The key is fetched first and deliberately not cached across attempts: rotating the VAPID
          // pair must not leave browsers subscribing against the old one, and this runs once per press.
          const subscription = await subscribe(await getVapidPublicKey())
          await postSubscription(subscription)
        }
        setEnabled(true)
      } catch (error) {
        setFailed(describe(error))
      } finally {
        inFlight.current = false
        setPending(false)
      }
    })()
  }

  return { enable, pending, enabled, unsupported, failed }
}

function describe(error: unknown): string {
  const reason: PushFailure = error instanceof PushError ? error.reason : 'failed'
  return PUSH_EXPLANATIONS[reason]
}
