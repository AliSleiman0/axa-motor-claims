import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import { useArrived } from './useArrived'

const ASSIGNMENT_ID = '00000000-0000-0000-0000-00000000a11c'
const ARRIVED_AT = '2026-08-20T09:30:00'

describe('useArrived', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn(
      () =>
        new Promise<Response>((resolve) =>
          setTimeout(
            () =>
              resolve(
                new Response(
                  JSON.stringify({ arrivedAt: ARRIVED_AT, latitude: 25.2048, longitude: 55.2708 }),
                  { status: 200 },
                ),
              ),
            0,
          ),
        ),
    )
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    Reflect.deleteProperty(navigator, 'geolocation')
  })

  it('sends the coordinates the browser gave it', async () => {
    givenPosition(25.2048, 55.2708)
    const { result } = render(null)

    act(() => result.current.press())

    await waitFor(() => expect(result.current.arrived).toBe(true))
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(`/api/expert/assignments/${ASSIGNMENT_ID}/arrival`)
    expect(init.method).toBe('POST')
    // Coordinates only: the arrival date and time are the server's clock (§5.1).
    expect(JSON.parse(init.body as string)).toEqual({ latitude: 25.2048, longitude: 55.2708 })
    expect(result.current.arrivedAt).toBe(ARRIVED_AT)
  })

  it('sends nothing when the browser refuses a location', async () => {
    // §5.1, the whole point: "arrival without location is not sent — the BRD requires all three
    // values". A request with no coordinates would be a half-arrival in NEXT3, which is worse than
    // none, so the guard is that no request leaves at all.
    givenDeniedPosition()
    const { result } = render(null)

    act(() => result.current.press())

    await waitFor(() => expect(result.current.blockedBy).toBe('denied'))
    expect(fetchMock).not.toHaveBeenCalled()
    expect(result.current.arrived).toBe(false)
  })

  it('issues one request when the button is pressed twice in the same tick', async () => {
    // The double tap that beats a re-render: `isPending` has not landed in state yet, so only the
    // latch inside the hook stops the second press.
    givenPosition(25.2048, 55.2708)
    const { result } = render(null)

    act(() => {
      result.current.press()
      result.current.press()
    })

    await waitFor(() => expect(result.current.arrived).toBe(true))
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('does nothing at all once the assignment has arrived', async () => {
    givenPosition(25.2048, 55.2708)
    const { result } = render(ARRIVED_AT)

    expect(result.current.arrived).toBe(true)
    act(() => result.current.press())

    await Promise.resolve()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('surfaces a failed request without blaming the location', async () => {
    givenPosition(25.2048, 55.2708)
    fetchMock.mockResolvedValue(new Response('{"error":"invalid_location"}', { status: 400 }))
    const { result } = render(null)

    act(() => result.current.press())

    await waitFor(() => expect(result.current.failed).toContain('400'))
    expect(result.current.blockedBy).toBeNull()
    expect(result.current.arrived).toBe(false)
  })

  function render(arrivedAt: string | null) {
    return renderHook(() => useArrived(ASSIGNMENT_ID, arrivedAt), { wrapper: TestQueryProvider })
  }
})

function givenPosition(latitude: number, longitude: number) {
  defineGeolocation((success) =>
    success({ coords: { latitude, longitude } } as GeolocationPosition),
  )
}

function givenDeniedPosition() {
  defineGeolocation((_success, failure) => failure?.({ code: 1 } as GeolocationPositionError))
}

function defineGeolocation(
  getCurrentPosition: (success: PositionCallback, failure?: PositionErrorCallback | null) => void,
) {
  Object.defineProperty(navigator, 'geolocation', {
    configurable: true,
    value: { getCurrentPosition: vi.fn(getCurrentPosition), watchPosition: vi.fn(), clearWatch: vi.fn() },
  })
}
