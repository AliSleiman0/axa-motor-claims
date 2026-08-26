import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { NATIVE_BATTERY_GUIDANCE } from './copy'
import { PushPanel } from './PushPanel'
import { PushError } from './subscription'

/**
 * Enabling notifications inside the Android shell (slice 6.3).
 *
 * **The bug this replaces is that the shell said push was impossible.** The Capacitor WebView
 * exposes no `PushManager`, so `readBrowserPushPermission()` answers `'unsupported'` there and the
 * panel rendered *"This browser cannot show claim notifications"* — inside the native app, whose
 * entire reason for existing is that it can. Unlike iOS there is no web-push fallback on Android, so
 * that message was the end of the road for the BRD's primary trigger on the platform most of the
 * fleet runs.
 *
 * Both seams are injected, so nothing here loads `@capacitor/push-notifications`.
 */
describe('the push panel in the native shell', () => {
  beforeEach(() => {
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string) =>
        Promise.resolve(
          url.includes('vapid-public-key')
            ? new Response(JSON.stringify({ publicKey: 'PLACEHOLDER-key' }), { status: 200 })
            : new Response(JSON.stringify({ id: 'PLACEHOLDER' }), { status: 200 }),
        ),
      ),
    )
  })

  it('offers the button even though the browser API says push is unsupported', () => {
    render(
      <PushPanel
        shellIsNative
        readPermission={() => 'unsupported'}
        acquireToken={vi.fn()}
        subscribe={vi.fn()}
      />,
    )

    // The regression that motivates the whole seam.
    expect(screen.getByRole('button', { name: 'Enable notifications' })).toBeDefined()
    expect(screen.queryByText(/cannot show claim notifications/i)).toBeNull()
  })

  it('registers a device token instead of a web subscription', async () => {
    const user = userEvent.setup({ delay: null })
    const acquireToken = vi.fn(() => Promise.resolve('PLACEHOLDER-fcm-token'))
    const subscribe = vi.fn()

    render(
      <PushPanel
        shellIsNative
        readPermission={() => 'unsupported'}
        acquireToken={acquireToken}
        subscribe={subscribe}
      />,
    )

    await user.click(screen.getByRole('button', { name: 'Enable notifications' }))

    await waitFor(() => expect(acquireToken).toHaveBeenCalledOnce())

    // **The service-worker path is never touched.** FCM identifies the app by the
    // google-services.json compiled into it and the server signs with a service account, so nothing
    // about web push is involved — no `subscribe`, and no VAPID key fetched.
    expect(subscribe).not.toHaveBeenCalled()

    const calls = vi.mocked(fetch).mock.calls.map(([url]) => String(url))
    expect(calls).toContain('/api/push/device-tokens')
    expect(calls.some((url) => url.includes('vapid-public-key'))).toBe(false)
  })

  it('surfaces a native failure without touching the service worker', async () => {
    const user = userEvent.setup({ delay: null })
    const subscribe = vi.fn()

    render(
      <PushPanel
        shellIsNative
        readPermission={() => 'unsupported'}
        // What a handset with no Google Play Services answers — every Huawei launched since 2020,
        // which `docs/oem-push-guidance.md` records as the one case FCM cannot serve at all (#28).
        acquireToken={() => Promise.reject(new PushError('unsupported'))}
        subscribe={subscribe}
      />,
    )

    await user.click(screen.getByRole('button', { name: 'Enable notifications' }))

    expect(await screen.findByRole('alert')).toBeDefined()
    expect(subscribe).not.toHaveBeenCalled()
  })

  it('shows battery guidance once notifications are on, and only in the shell', async () => {
    const user = userEvent.setup({ delay: null })

    const { unmount } = render(
      <PushPanel
        shellIsNative
        readPermission={() => 'unsupported'}
        acquireToken={() => Promise.resolve('PLACEHOLDER-fcm-token')}
      />,
    )

    // Not before: told while the button is still unpressed, it is advice about something that is not
    // happening yet, on a screen already asking for one decision.
    expect(screen.queryByText(NATIVE_BATTERY_GUIDANCE)).toBeNull()

    await user.click(screen.getByRole('button', { name: 'Enable notifications' }))
    expect(await screen.findByText(NATIVE_BATTERY_GUIDANCE)).toBeDefined()

    unmount()

    // A desktop browser is not subject to OEM battery management, and saying so there would be
    // instructions for a Settings screen that does not exist.
    render(<PushPanel readPermission={() => 'granted'} readSubscription={() => Promise.resolve(true)} />)
    await waitFor(() => expect(screen.getByRole('status')).toBeDefined())
    expect(screen.queryByText(NATIVE_BATTERY_GUIDANCE)).toBeNull()
  })

  it('never claims notifications are already on before the button is pressed', () => {
    render(
      <PushPanel
        shellIsNative
        readPermission={() => 'granted'}
        readSubscription={() => Promise.resolve(true)}
        acquireToken={vi.fn()}
      />,
    )

    // The shell cannot ask a service worker anything, and inferring "on" from the OS permission
    // would say "Notifications are on" while the server held no row — the defect already on 7.2's
    // list for the browser, and not worth reproducing here. Re-registering is an upsert, so
    // offering the button costs nothing.
    expect(screen.getByRole('button', { name: 'Enable notifications' })).toBeDefined()
  })
})
