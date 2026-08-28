import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PushError, type BrowserSubscription } from './subscription'
import { usePushSubscription } from './usePushSubscription'

const SUBSCRIPTION: BrowserSubscription = {
  endpoint: 'https://push.example.invalid/PLACEHOLDER-endpoint',
  p256dh: 'PLACEHOLDER-p256dh',
  auth: 'PLACEHOLDER-auth',
}

describe('usePushSubscription', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn((url: string) =>
      Promise.resolve(
        url.includes('vapid-public-key')
          ? new Response(JSON.stringify({ publicKey: 'PLACEHOLDER-key' }), { status: 200 })
          : new Response(JSON.stringify({ id: '00000000-0000-0000-0000-000000000001' }), { status: 200 }),
      ),
    )
    vi.stubGlobal('fetch', fetchMock)
  })

  it('makes no subscription attempt until the button is pressed', async () => {
    const subscribe = vi.fn()

    renderHook(() => usePushSubscription({ subscribe, readPermission: () => 'default' }))
    await Promise.resolve()

    // The reason this hook has a button at all: Notification.requestPermission() needs a user
    // gesture, and asking without one is either refused outright or counted against the origin.
    // A hook that subscribed on mount would look fine in jsdom and poison the site in Chrome.
    expect(subscribe).not.toHaveBeenCalled()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('reports notifications as already on when this browser is subscribed', async () => {
    const subscribe = vi.fn()

    const { result } = renderHook(() =>
      usePushSubscription({
        subscribe,
        readPermission: () => 'granted',
        resync: () => Promise.resolve(true),
      }),
    )

    await waitFor(() => expect(result.current.enabled).toBe(true))

    // The bug this pins was found in a real browser and was invisible to every test above, because
    // they all mounted with nothing subscribed: the panel offered "Enable notifications" on every
    // page load to an expert who had already enabled them. Reading the existing subscription prompts
    // for nothing, so it is safe on mount in a way that subscribing is not.
    expect(subscribe).not.toHaveBeenCalled()
  })

  it('does not ask the service worker anything before permission is granted', async () => {
    const resync = vi.fn(() => Promise.resolve(false))

    renderHook(() => usePushSubscription({ readPermission: () => 'default', resync }))
    await Promise.resolve()

    // A subscription cannot exist without permission, so asking is pointless work on first paint —
    // and on a browser that has never registered a worker the question can hang.
    expect(resync).not.toHaveBeenCalled()
  })

  it('still offers the button when the subscription probe fails', async () => {
    const { result } = renderHook(() =>
      usePushSubscription({
        subscribe: vi.fn(),
        readPermission: () => 'granted',
        resync: () => Promise.reject(new Error('service worker unavailable')),
      }),
    )

    await Promise.resolve()
    await Promise.resolve()

    // It only chooses between two labels. A browser that cannot answer should get the button, not an
    // error it cannot act on.
    expect(result.current.enabled).toBe(false)
    expect(result.current.failed).toBeNull()
  })

  it('fetches the key, subscribes and registers the browser when pressed', async () => {
    const subscribe = vi.fn(() => Promise.resolve(SUBSCRIPTION))

    const { result } = renderHook(() =>
      usePushSubscription({ subscribe, readPermission: () => 'default' }),
    )
    act(() => result.current.enable())

    await waitFor(() => expect(result.current.enabled).toBe(true))

    expect(subscribe).toHaveBeenCalledWith('PLACEHOLDER-key')

    const [url, init] = fetchMock.mock.calls[1] as [string, RequestInit]
    expect(url).toBe('/api/push/subscriptions')
    expect(init.method).toBe('POST')
    expect(JSON.parse(String(init.body))).toEqual(SUBSCRIPTION)
    expect(result.current.failed).toBeNull()
  })

  it('issues one attempt when the button is pressed twice in the same tick', async () => {
    const subscribe = vi.fn(() => Promise.resolve(SUBSCRIPTION))

    const { result } = renderHook(() =>
      usePushSubscription({ subscribe, readPermission: () => 'default' }),
    )

    // Both calls run before React re-renders, so `pending` is still false for the second — only the
    // useRef latch stops it. 1.5's lesson, fifth slice running; removing the latch turns this into
    // two permission prompts and two POSTs.
    act(() => {
      result.current.enable()
      result.current.enable()
    })

    await waitFor(() => expect(result.current.enabled).toBe(true))
    expect(subscribe).toHaveBeenCalledTimes(1)
  })

  it('offers nothing when the browser cannot do push at all', async () => {
    const subscribe = vi.fn()

    const { result } = renderHook(() =>
      usePushSubscription({ subscribe, readPermission: () => 'unsupported' }),
    )
    act(() => result.current.enable())
    await Promise.resolve()

    expect(result.current.unsupported).toBe(true)
    expect(subscribe).not.toHaveBeenCalled()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it.each([
    ['denied', 'choose Allow'],
    ['blocked', 'padlock in the address bar'],
    ['failed', 'Check the connection'],
  ] as const)('explains a %s subscription so the expert knows what to do', async (reason, advice) => {
    const subscribe = vi.fn(() => Promise.reject(new PushError(reason)))

    const { result } = renderHook(() =>
      usePushSubscription({ subscribe, readPermission: () => 'default' }),
    )
    act(() => result.current.enable())

    await waitFor(() => expect(result.current.failed).not.toBeNull())

    // `blocked` and `denied` are separate for a reason: telling somebody whose browser has
    // permanently blocked the site to "press again and choose Allow" sends them round a loop that
    // cannot terminate, at a crash site.
    expect(result.current.failed).toContain(advice)
    expect(result.current.enabled).toBe(false)
  })

  it('reports an API failure rather than claiming notifications are on', async () => {
    fetchMock.mockImplementation((url: string) =>
      Promise.resolve(
        url.includes('vapid-public-key')
          ? new Response(JSON.stringify({ publicKey: 'PLACEHOLDER-key' }), { status: 200 })
          : new Response('', { status: 500 }),
      ),
    )

    const { result } = renderHook(() =>
      usePushSubscription({
        subscribe: () => Promise.resolve(SUBSCRIPTION),
        readPermission: () => 'default',
      }),
    )
    act(() => result.current.enable())

    await waitFor(() => expect(result.current.failed).not.toBeNull())

    // The browser now holds a subscription the server does not know about. Saying "notifications are
    // on" here would be a lie the expert only discovers by not being told about a claim.
    expect(result.current.enabled).toBe(false)
  })

  it('releases the latch after a failure so the expert can try again', async () => {
    const subscribe = vi
      .fn<() => Promise<BrowserSubscription>>()
      .mockRejectedValueOnce(new PushError('failed'))
      .mockResolvedValueOnce(SUBSCRIPTION)

    const { result } = renderHook(() =>
      usePushSubscription({ subscribe, readPermission: () => 'default' }),
    )

    act(() => result.current.enable())
    await waitFor(() => expect(result.current.failed).not.toBeNull())

    act(() => result.current.enable())
    await waitFor(() => expect(result.current.enabled).toBe(true))

    expect(subscribe).toHaveBeenCalledTimes(2)
  })

  /**
   * **The push lie, fixed** (browser-pass finding 8, `demo-fix-list` #16, slice 7.2).
   *
   * The mount effect used to ask the browser whether it held a subscription and believe the answer.
   * A browser's subscription outlives the server's record of it — a database restore, a re-created
   * user, a rotated VAPID pair, a second person signing in on the same profile — so the panel said
   * "Notifications are on" while `push_subscription` held zero rows, and the popup silently never
   * arrived. Re-posting what the browser holds makes the two facts one fact.
   */
  it('re-posts the browser subscription on mount and only then says notifications are on', async () => {
    const resync = vi.fn(() => Promise.resolve(true))

    const { result } = renderHook(() =>
      usePushSubscription({ subscribe: vi.fn(), readPermission: () => 'granted', resync }),
    )

    await waitFor(() => expect(result.current.enabled).toBe(true))
    expect(resync).toHaveBeenCalledTimes(1)
  })

  /**
   * The half that makes the fix honest. If the server did not accept the resync, nothing confirms
   * that AXA can notify this browser — so the panel offers the button rather than repeating the
   * claim it was just unable to verify. One press is all it costs, and the press is what the user
   * would have had to do anyway.
   */
  it('leaves the button offered when the server does not accept the resync', async () => {
    const { result } = renderHook(() =>
      usePushSubscription({
        subscribe: vi.fn(),
        readPermission: () => 'granted',
        resync: () => Promise.reject(new Error('401')),
      }),
    )

    await waitFor(() => expect(result.current.pending).toBe(false))

    expect(result.current.enabled).toBe(false)
  })

})
