import { describe, expect, it, vi } from 'vitest'
import {
  explainGeolocationFailure,
  GEOLOCATION_EXPLANATIONS,
  GeolocationError,
  requestPosition,
} from './geolocation'

/** The four ways a position request ends, mapped to the reasons E2 explains (design.md §5.1). */
describe('requestPosition', () => {
  it('resolves to the coordinates the browser reports', async () => {
    const geolocation = stub((success) =>
      success({ coords: { latitude: 25.2048, longitude: 55.2708 } } as GeolocationPosition),
    )

    await expect(requestPosition(geolocation)).resolves.toEqual({
      latitude: 25.2048,
      longitude: 55.2708,
    })
  })

  it.each([
    [1, 'denied'],
    [2, 'unavailable'],
    [3, 'timeout'],
  ])('maps error code %i to "%s"', async (code, reason) => {
    const geolocation = stub((_success, failure) =>
      failure?.({ code } as GeolocationPositionError),
    )

    await expect(requestPosition(geolocation)).rejects.toMatchObject({ reason })
  })

  it('reports "unsupported" when the browser has no geolocation at all', async () => {
    // Not a hypothetical: a desktop browser with location disabled at the OS level, and every
    // non-secure context. It must not read as a denial — the fix is a different one.
    const error = await requestPosition(undefined).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(GeolocationError)
    expect((error as GeolocationError).reason).toBe('unsupported')
  })
})

function stub(
  getCurrentPosition: (
    success: PositionCallback,
    failure?: PositionErrorCallback | null,
  ) => void,
): Geolocation {
  return {
    getCurrentPosition: vi.fn(getCurrentPosition),
    watchPosition: vi.fn(),
    clearWatch: vi.fn(),
  }
}

describe('the shell-aware failure copy (slice 6.3)', () => {
  it('tells a native user the location setting is probably off', () => {
    const native = explainGeolocationFailure('timeout', true)

    // **6.3a measured this on the Samsung: with location services off, the Android WebView reports
    // code 3, TIMEOUT** — not POSITION_UNAVAILABLE. So the expert was told, accurately, what the
    // platform said, and told to do a thing that can never work. A web page cannot read the system
    // setting, so the copy has to name the likelier cause.
    expect(native).toContain('Location is switched off')
    expect(native).toContain('Settings')
    expect(native).not.toContain('once the device has a signal')
  })

  it('leaves the browser copy exactly as it was', () => {
    // Every desktop role and the iOS PWA still get the original sentence, where a timeout really is
    // a timeout. Asserted against the shipped table rather than restated, so the two cannot drift.
    expect(explainGeolocationFailure('timeout', false)).toBe(GEOLOCATION_EXPLANATIONS.timeout)
  })

  it('changes nothing about the other three codes', () => {
    // Only `timeout` is overridden. Denied, unavailable and unsupported mean the same thing on both
    // platforms, and a second full table would be two copies to keep in step.
    for (const reason of ['denied', 'unavailable', 'unsupported'] as const) {
      expect(explainGeolocationFailure(reason, true)).toBe(GEOLOCATION_EXPLANATIONS[reason])
    }
  })
})
