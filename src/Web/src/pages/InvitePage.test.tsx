import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getTokens } from '../api/tokens'
import InvitePage from './InvitePage'

const TOKEN = 'PLACEHOLDER-INVITE-TOKEN-0000000000'

const TOKENS = {
  accessToken: `header.${btoa(JSON.stringify({ role: 'garage', sub: 'u1' }))}.sig`,
  refreshToken: 'PLACEHOLDER.refresh',
}

describe('S1 — activating an invitation', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('shows the invitation the link carried, rather than asking for it', async () => {
    stub()
    show(`/invite/${TOKEN}`)

    expect(screen.getByRole('heading', { name: 'You have been invited' })).toBeDefined()
    expect(screen.getByText(TOKEN)).toBeDefined()
    expect(screen.queryByLabelText('Invitation')).toBeNull()
  })

  it('asks for the invitation when the link was not used', async () => {
    // The SMS carries a URL now, but carriers strip them, and somebody may be reading the text on a
    // handset while registering on a laptop.
    stub()
    show('/invite')

    expect(screen.getByLabelText('Invitation')).toBeDefined()
    // Nothing to send yet: an empty paste box must not fire a request.
    expect(screen.getByRole('button', { name: 'Send my code' }).hasAttribute('disabled')).toBe(true)
  })

  it('cannot show which number the code went to, and says the honest thing instead', async () => {
    // The invitation is the only thing this browser holds, and the API returns nothing identifying
    // before activation. A masked number would need a new endpoint and a decision about what it
    // reveals — so the screen describes the profile rather than naming a number it does not have.
    const user = userEvent.setup()
    stub()
    show(`/invite/${TOKEN}`)

    await user.click(screen.getByRole('button', { name: 'Send my code' }))

    expect(await screen.findByRole('heading', { name: 'Confirm your number' })).toBeDefined()
    expect(screen.getByText(/mobile number on your profile/)).toBeDefined()
  })

  it('activates and signs in, then names the destination for the role', async () => {
    // Activation and sign-in are one step — verifying the code turns the profile active *and* hands
    // back a session, so there is no separate "now log in". The button names where a garage goes.
    const user = userEvent.setup()
    stub()
    show(`/invite/${TOKEN}`)

    await user.click(screen.getByRole('button', { name: 'Send my code' }))
    await user.type(await screen.findByLabelText('Code'), '123456')
    await user.click(screen.getByRole('button', { name: 'Activate my account' }))

    expect(await screen.findByRole('heading', { name: 'You are signed in' })).toBeDefined()
    expect(screen.getByText(/this invitation will not work again/)).toBeDefined()
    expect(getTokens()).toEqual(TOKENS)

    await user.click(screen.getByRole('button', { name: 'Go to My declarations' }))
    expect(screen.getByTestId('location').textContent).toBe('/garage')
  })

  it('offers the same code field a phone can autofill', async () => {
    const user = userEvent.setup()
    stub()
    show(`/invite/${TOKEN}`)
    await user.click(screen.getByRole('button', { name: 'Send my code' }))

    const field = await screen.findByLabelText('Code')
    expect(field.getAttribute('autocomplete')).toBe('one-time-code')
    expect(field.getAttribute('inputmode')).toBe('numeric')
  })

  describe('a rejected code', () => {
    it('says so, and leaves the invitation itself usable', async () => {
      // Only the code failed. A new code does not consume the invitation, so the way forward is
      // "Send a new code" and not "ask for a new invitation".
      const user = userEvent.setup()
      stub({ verifyStatus: 401 })
      show(`/invite/${TOKEN}`)

      await user.click(screen.getByRole('button', { name: 'Send my code' }))
      await user.type(await screen.findByLabelText('Code'), '000000')
      await user.click(screen.getByRole('button', { name: 'Activate my account' }))

      expect((await screen.findByRole('alert')).textContent).toContain('That code was not accepted')
      expect(screen.getByRole('button', { name: 'Send a new code' })).toBeDefined()
    })

    it('names the five-attempt rule once the screen has counted five', async () => {
      const user = userEvent.setup()
      stub({ verifyStatus: 401 })
      show(`/invite/${TOKEN}`)

      await user.click(screen.getByRole('button', { name: 'Send my code' }))
      const field = await screen.findByLabelText('Code')
      for (let attempt = 0; attempt < 5; attempt++) {
        await user.clear(field)
        await user.type(field, '00000' + String(attempt))
        await user.click(screen.getByRole('button', { name: 'Activate my account' }))
      }

      expect(screen.getByRole('alert').textContent).toContain('can be tried five times')
    })
  })

  describe('an invitation that cannot be used', () => {
    it('gives one screen for all three causes, and a way out through a person', async () => {
      // Expired, already used and never valid are one bare 404 on purpose — naming which would let
      // anyone test invitations to see which exist. So the screen says "may", and the way forward is
      // an admin re-issuing rather than a retry that cannot work.
      const user = userEvent.setup()
      stub({ acceptStatus: 404 })
      show(`/invite/${TOKEN}`)

      await user.click(screen.getByRole('button', { name: 'Send my code' }))

      expect(
        await screen.findByRole('heading', { name: 'This invitation cannot be used' }),
      ).toBeDefined()
      expect(screen.getByText(/It may have expired, or it may already have been used/)).toBeDefined()
      // Offered because an already-used invitation usually means the account is active — the person
      // is not stuck, they just need the other door.
      expect(screen.getByRole('link', { name: 'Go to sign in' }).getAttribute('href')).toBe('/login')
    })

    it('catches an invitation that lapses while the code is being typed', async () => {
      // Seven days is long enough for the window to close mid-flow, and a 404 from *verify* means
      // the invitation went — not that the code was wrong. Telling the person to try another code
      // would send them round a loop that cannot end.
      const user = userEvent.setup()
      stub({ verifyStatus: 404 })
      show(`/invite/${TOKEN}`)

      await user.click(screen.getByRole('button', { name: 'Send my code' }))
      await user.type(await screen.findByLabelText('Code'), '123456')
      await user.click(screen.getByRole('button', { name: 'Activate my account' }))

      expect(
        await screen.findByRole('heading', { name: 'This invitation cannot be used' }),
      ).toBeDefined()
    })
  })

  it('counts the resend down from the seconds the server sent', async () => {
    const user = userEvent.setup()
    stub({ acceptStatus: 429, retryAfter: '42' })
    show(`/invite/${TOKEN}`)

    await user.click(screen.getByRole('button', { name: 'Send my code' }))

    expect(await screen.findByText('Resend in 42 s')).toBeDefined()
    // Throttled is not refused: a code was just sent and is still good, so the screen moves on.
    expect(screen.getByLabelText('Code')).toBeDefined()
    expect(screen.queryByRole('alert')).toBeNull()
  })

  function stub({
    acceptStatus = 200,
    retryAfter = null,
    verifyStatus = 200,
  }: { acceptStatus?: number; retryAfter?: string | null; verifyStatus?: number } = {}) {
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string) => {
        if (String(url).endsWith('/auth/invite/accept')) {
          const headers = retryAfter ? { 'Retry-After': retryAfter } : undefined
          return Promise.resolve(new Response('', { status: acceptStatus, headers }))
        }
        return Promise.resolve(
          verifyStatus === 200
            ? new Response(JSON.stringify(TOKENS), { status: 200 })
            : new Response('', { status: verifyStatus }),
        )
      }),
    )
  }
})

function show(entry: string) {
  render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/invite" element={<InvitePage />} />
        <Route path="/invite/:token" element={<InvitePage />} />
        <Route path="/garage" element={<p>PLACEHOLDER declarations</p>} />
        <Route path="/login" element={<p>PLACEHOLDER sign in</p>} />
      </Routes>
      <LocationProbe />
    </MemoryRouter>,
  )
}

function LocationProbe() {
  return <span data-testid="location">{useLocation().pathname}</span>
}
