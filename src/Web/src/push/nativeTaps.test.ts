import { afterEach, describe, expect, it, vi } from 'vitest'
import { wireNativeNotificationTaps } from './nativeTaps'

/**
 * Deep-linking a tapped FCM notification (slice 6.3).
 *
 * `@capacitor/push-notifications` is reached through a dynamic `import()`, which `vi.mock` can
 * intercept — so the listener contract is testable here even though the real plugin cannot load in
 * jsdom. What is being pinned is the tap contract itself: **`data.url` is the same field
 * `public/sw.js` reads**, so a push deep-links identically whether it arrived through FCM or through
 * web push, and §8's four still-to-come events need no second contract.
 */
const listeners: Record<string, (event: unknown) => void> = {}
const remove = vi.fn(() => Promise.resolve())

vi.mock('@capacitor/push-notifications', () => ({
  PushNotifications: {
    addListener: vi.fn((event: string, handler: (payload: unknown) => void) => {
      listeners[event] = handler
      return Promise.resolve({ remove })
    }),
  },
}))

function tap(data: unknown) {
  listeners['pushNotificationActionPerformed']({ notification: { data } })
}

describe('native notification taps', () => {
  afterEach(() => {
    for (const key of Object.keys(listeners)) delete listeners[key]
    remove.mockClear()
  })

  it('navigates to the url the notification carried', async () => {
    const navigate = vi.fn()
    await wireNativeNotificationTaps(navigate)

    tap({ url: '/expert/00000000-0000-0000-0000-00000000a11c' })

    // The exact contract `PushMessage.Url` writes and `AssignmentHandler` fills with
    // `/expert/{assignmentId}` — an expert taps the popup at a crash site and lands on that claim,
    // not on a list they then have to search.
    expect(navigate).toHaveBeenCalledWith('/expert/00000000-0000-0000-0000-00000000a11c')
  })

  it('falls back to the root when there is no url', async () => {
    const navigate = vi.fn()
    await wireNativeNotificationTaps(navigate)

    tap({})

    // Doing nothing would read as a broken app. `sw.js` already resolves `payload.url || '/'` the
    // same way, so both channels land somewhere useful rather than nowhere.
    expect(navigate).toHaveBeenCalledWith('/')
  })

  it('falls back to the root when the url is not a string', async () => {
    const navigate = vi.fn()
    await wireNativeNotificationTaps(navigate)

    tap({ url: 42 })

    // `data` comes off the wire as whatever FCM delivered, so it is checked rather than trusted:
    // navigating to a non-string would throw inside the tap handler, where nothing catches it.
    expect(navigate).toHaveBeenCalledWith('/')
  })

  it('registers only the tap listener, never a foreground display', async () => {
    await wireNativeNotificationTaps(vi.fn())

    // **The smaller interpretation, made checkable.** Android suppresses its own tray notification
    // while the app is in the foreground, and re-displaying one in-app would be inventing UI the BRD
    // does not describe over a screen the expert is already looking at. Recorded in
    // scope-decisions.md; a `pushNotificationReceived` listener appearing here is the change that
    // should make somebody re-read that entry.
    expect(Object.keys(listeners)).toEqual(['pushNotificationActionPerformed'])
  })

  it('stops listening when told to', async () => {
    const detach = await wireNativeNotificationTaps(vi.fn())

    detach()

    // The effect in `App.tsx` returns this. Without it, StrictMode's remount in development would
    // leave a handler behind and a single tap would navigate twice.
    expect(remove).toHaveBeenCalledOnce()
  })
})
