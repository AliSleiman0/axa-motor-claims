import { describe, expect, it, vi } from 'vitest'
import { GeolocationError, requestPosition } from './geolocation'

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
