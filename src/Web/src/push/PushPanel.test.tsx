import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PushPanel } from './PushPanel'
import { PushError, type BrowserSubscription } from './subscription'

const SUBSCRIPTION: BrowserSubscription = {
  endpoint: 'https://push.example.invalid/PLACEHOLDER-endpoint',
  p256dh: 'PLACEHOLDER-p256dh',
  auth: 'PLACEHOLDER-auth',
}

describe('the Enable notifications panel', () => {
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

  it('offers the button when notifications have not been enabled yet', () => {
    render(<PushPanel subscribe={vi.fn()} readPermission={() => 'default'} />)

    expect(screen.getByRole('button', { name: 'Enable notifications' })).toBeDefined()
  })

  it('confirms once the browser is registered', async () => {
    const user = userEvent.setup({ delay: null })
    render(
      <PushPanel subscribe={() => Promise.resolve(SUBSCRIPTION)} readPermission={() => 'default'} />,
    )

    await user.click(screen.getByRole('button', { name: 'Enable notifications' }))

    await waitFor(() => expect(screen.getByRole('status')).toBeDefined())
    expect(screen.getByRole('status').textContent).toContain('Notifications are on')
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('shows the explanation as an alert when the browser refuses', async () => {
    const user = userEvent.setup({ delay: null })
    render(
      <PushPanel
        subscribe={() => Promise.reject(new PushError('blocked'))}
        readPermission={() => 'default'}
      />,
    )

    await user.click(screen.getByRole('button', { name: 'Enable notifications' }))

    await waitFor(() => expect(screen.getByRole('alert')).toBeDefined())
    expect(screen.getByRole('alert').textContent).toContain('padlock in the address bar')
    // Still offered, because the expert can fix this and come back.
    expect(screen.getByRole('button', { name: 'Enable notifications' })).toBeDefined()
  })

  it('offers no button at all on a browser that cannot do push', () => {
    render(<PushPanel subscribe={vi.fn()} readPermission={() => 'unsupported'} />)

    // A button guaranteed to fail is worse than none: it invites an expert at a crash site to keep
    // pressing something that can never work.
    expect(screen.queryByRole('button')).toBeNull()
    expect(screen.getByRole('status').textContent).toContain('cannot show claim notifications')
  })

  it('disables the button while the subscription is in flight', async () => {
    const user = userEvent.setup({ delay: null })
    let release: (value: BrowserSubscription) => void = () => {}
    render(
      <PushPanel
        subscribe={() => new Promise<BrowserSubscription>((resolve) => (release = resolve))}
        readPermission={() => 'default'}
      />,
    )

    await user.click(screen.getByRole('button', { name: 'Enable notifications' }))

    // Both halves matter: the label says something is happening, and the button cannot be pressed
    // again. The useRef latch in the hook is what actually stops a second permission prompt — this
    // is the part the expert can see.
    const button = screen.getByRole('button', { name: 'Enabling notifications…' }) as HTMLButtonElement
    expect(button.disabled).toBe(true)

    release(SUBSCRIPTION)
    await waitFor(() => expect(screen.getByRole('status')).toBeDefined())
  })
})
