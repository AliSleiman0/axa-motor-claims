import { useEffect } from 'react'
import { detectNativeShell } from '../media/nativeShell'
import { wireNativeNotificationTaps, type NavigateTo } from './nativeTaps'

export interface UseNativeNotificationTapsOptions {
  /** Whether a native shell is present. Injectable so a test needs no Capacitor bridge. */
  isNative?: () => boolean
  /** The wiring seam itself, for the same reason. */
  wire?: typeof wireNativeNotificationTaps
}

/**
 * Keeps a native notification-tap listener attached for as long as this is mounted (slice 6.3).
 *
 * **This is a hook rather than four lines inside `App.tsx` because those four lines shipped a bug
 * that only a real handset could show, and a hook is the smallest thing a test can mount twice.**
 *
 * The first version held a `useRef` latch so the listener was wired "only once", on the reasoning
 * that StrictMode double-invokes effects and would otherwise attach two. That reasoning was wrong in
 * a way that inverted the outcome. StrictMode does not run the effect twice back to back — it runs
 * it, *unmounts*, and runs it again. So the sequence was: wire → cleanup removes the listener →
 * remount sees the latch already set and returns early. The app then ran with **no listener at
 * all**, and every notification tap opened the worklist instead of the claim. Observed on the
 * Samsung during 6.3's device pass; invisible to every test, because nothing mounted this twice.
 *
 * The cleanup *is* the guard. Wiring on mount and unwiring on unmount is symmetric and cannot leak,
 * whether React mounts once or a hundred times.
 */
export function useNativeNotificationTaps(
  navigate: NavigateTo,
  { isNative = () => detectNativeShell() !== null, wire = wireNativeNotificationTaps }:
    UseNativeNotificationTapsOptions = {},
): void {
  useEffect(() => {
    // The probe, not a try/catch around the import: in a browser there is no Capacitor bridge and
    // the dynamic import must never be reached, so the async chunk is never fetched.
    if (!isNative()) {
      return undefined
    }

    let cancelled = false
    let detach: (() => void) | undefined

    void wire((url) => navigate(url)).then((stop) => {
      // **The cleanup can run while the dynamic import is still in flight** — under StrictMode it
      // reliably does. Without this the listener attaches *after* its own cleanup has run and is
      // never removed, which is the leak the original latch was reaching for and the wrong fix for.
      if (cancelled) {
        stop()
      } else {
        detach = stop
      }
    })

    return () => {
      cancelled = true
      detach?.()
    }
  }, [navigate, isNative, wire])
}
