import { afterEach, describe, expect, it } from 'vitest'
import { detectNativeShell, NativeCaptureCancelled } from './nativeShell'

/**
 * The shell probe (slice 6.3).
 *
 * Only the probe is tested here, deliberately: `takePhoto` reaches for `@capacitor/camera`, which
 * cannot be loaded in jsdom and is pinned as unreachable by `nativeImports.test.ts` instead. What a
 * test *can* prove is that a browser never gets that far, which is the property everything else
 * depends on — every call site takes the shell as a parameter precisely so the camera itself can be
 * exercised on a real handset and nowhere else.
 */
describe('detecting the native shell', () => {
  afterEach(() => {
    Reflect.deleteProperty(globalThis, 'Capacitor')
  })

  it('finds no shell in an ordinary browser', () => {
    // The state every web test, every desktop role and every PWA install runs in.
    expect(detectNativeShell()).toBeNull()
  })

  it('finds a shell when the Capacitor bridge says it is native', () => {
    Object.defineProperty(globalThis, 'Capacitor', {
      value: { isNativePlatform: () => true },
      configurable: true,
    })

    const shell = detectNativeShell()

    expect(shell).not.toBeNull()
    expect(shell!.hasCamera).toBe(true)
  })

  it('finds no shell when Capacitor is present but not native', () => {
    // `@capacitor/core` injects the bridge into a *browser* too when the web build is loaded through
    // it. Treating "the object exists" as "this is a phone" would put the camera button on a desktop
    // and take the file input away from a role that needs one.
    Object.defineProperty(globalThis, 'Capacitor', {
      value: { isNativePlatform: () => false },
      configurable: true,
    })

    expect(detectNativeShell()).toBeNull()
  })

  it('finds no shell when the bridge has an unfamiliar shape', () => {
    // Older and newer Capacitor versions have disagreed about whether `isNativePlatform` is a method
    // or a value. The probe is optional-called rather than assumed, so an unrecognised shape falls
    // back to the browser path instead of throwing on first paint.
    Object.defineProperty(globalThis, 'Capacitor', { value: {}, configurable: true })

    expect(detectNativeShell()).toBeNull()
  })

  it('names cancellation as its own type', () => {
    // Closing the camera without taking anything is ordinary. It has a type so `CapturePanel` can
    // tell it apart from a real failure and show nothing rather than an error banner.
    expect(new NativeCaptureCancelled()).toBeInstanceOf(Error)
    expect(new NativeCaptureCancelled().name).toBe('NativeCaptureCancelled')
  })
})
