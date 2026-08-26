import { act, render } from '@testing-library/react'
import { StrictMode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { useNativeNotificationTaps } from './useNativeNotificationTaps'

/**
 * The notification-tap wiring (slice 6.3).
 *
 * **Every test here exists because of one defect the device pass found and nothing else could.** The
 * first version of this wiring held a "wire only once" ref latch. StrictMode does not run an effect
 * twice back to back — it runs it, unmounts, and runs it again — so the sequence was wire → cleanup
 * removes the listener → remount returns early on the latch. The app ran with **no listener at all**
 * and every notification tap opened the worklist instead of the claim, on the one screen an expert
 * reaches by tapping a popup at a crash site.
 *
 * No test could see it, because nothing mounted the wiring twice. So these mount it twice.
 *
 * **Every assertion here is made on the *settled* state, after `flush()`, never through
 * `waitFor`.** The first version of this file used `await vi.waitFor(() => expect(live).toBe(1))`
 * and passed against the very bug it was written for: with the latch in place the listener really is
 * attached for a moment before its own cleanup removes it, and `waitFor` succeeds the instant it
 * sees what it wants. A test that accepts a transient cannot fail on a teardown — the same shape as
 * 2.4's arithmetic-back-to-itself, one layer up. Verified by planting the original latch: the
 * StrictMode test goes red, and only with the settled-state assertion — with `waitFor` it passed.
 */

/** Settles every pending microtask, so an assertion sees the end state and not a moment inside it. */
async function flush() {
  await act(async () => {
    await Promise.resolve()
    await Promise.resolve()
  })
}
function Harness({
  navigate = vi.fn(),
  isNative = () => true,
  wire,
}: {
  navigate?: (url: string) => void
  isNative?: () => boolean
  wire: typeof import('./nativeTaps').wireNativeNotificationTaps
}) {
  useNativeNotificationTaps(navigate, { isNative, wire })
  return null
}

/** A wire seam that counts what is attached and what is still live. */
function tracker() {
  let live = 0
  const wire = vi.fn(() => {
    live += 1
    return Promise.resolve(() => {
      live -= 1
    })
  })
  return { wire, live: () => live, attaches: () => wire.mock.calls.length }
}

describe('keeping the tap listener attached', () => {
  it('wires once on a plain mount', async () => {
    const t = tracker()
    render(<Harness wire={t.wire} />)
    await flush()
    expect(t.live()).toBe(1)
  })

  it('survives StrictMode, which mounts, unmounts and mounts again', async () => {
    const t = tracker()

    render(
      <StrictMode>
        <Harness wire={t.wire} />
      </StrictMode>,
    )

    // **The regression test.** With the old ref latch this settles at 0 — attached once, removed by
    // StrictMode's cleanup, and never re-attached because the latch was already set. One live
    // listener is the whole requirement: not zero (taps do nothing) and not two (every tap
    // navigates twice).
    await flush()
    expect(t.live()).toBe(1)
  })

  it('re-wires after a real unmount and remount', async () => {
    const t = tracker()

    const first = render(<Harness wire={t.wire} />)
    await flush()
    expect(t.live()).toBe(1)

    first.unmount()
    await flush()
    expect(t.live()).toBe(0)

    render(<Harness wire={t.wire} />)
    await flush()
    expect(t.live()).toBe(1)
    expect(t.attaches()).toBe(2)
  })

  it('removes a listener that arrives after its own cleanup', async () => {
    // The async race the latch was really reaching for: the dynamic import is in flight when the
    // effect is torn down. Resolving afterwards must not leave a listener nothing will ever remove.
    let resolve!: (stop: () => void) => void
    const wire = vi.fn(() => new Promise<() => void>((r) => { resolve = r }))

    const view = render(<Harness wire={wire} />)
    view.unmount()

    // The listener "attaches" only now, after the effect has already been torn down.
    let live = 1
    resolve(() => { live = 0 })

    await flush()
    expect(live).toBe(0)
  })

  it('does not reach for Capacitor in a browser', async () => {
    const t = tracker()

    render(<Harness wire={t.wire} isNative={() => false} />)

    // Every desktop role and the iOS PWA take this path. Touching the seam at all would fetch the
    // async chunk `nativeImports.test.ts` exists to keep out of the main bundle.
    await flush()
    expect(t.attaches()).toBe(0)
  })
})
