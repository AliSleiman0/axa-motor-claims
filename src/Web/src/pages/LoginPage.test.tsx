import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { getTokens } from '../api/tokens'
import LoginPage from './LoginPage'

const PHONE = '+999000003001'

/** A real expert token — `homePathFor` routes on the role claim, so the payload has to carry one. */
const TOKENS = {
  accessToken: `header.${btoa(JSON.stringify({ role: 'expert', sub: 'u1' }))}.sig`,
  refreshToken: 'PLACEHOLDER.refresh',
}

describe('S2 — sign in', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('asks for a number first, and only then for a code', async () => {
    const user = userEvent.setup()
    stub()
    show()

    expect(screen.getByRole('heading', { name: 'Sign in' })).toBeDefined()
    expect(screen.queryByLabelText('Code')).toBeNull()

    await sendCode(user)

    expect(await screen.findByRole('heading', { name: 'Enter the code' })).toBeDefined()
    // The number is named, because somebody who mistyped a digit needs to see it before they go
    // looking for a text that was never going to arrive.
    expect(screen.getByText(PHONE)).toBeDefined()
  })

  it('offers one code field that a phone can autofill', async () => {
    // One field, not six boxes: a pasted or autofilled code has to land somewhere. `one-time-code`
    // is what lets a handset offer the code from the notification without leaving the app.
    const user = userEvent.setup()
    stub()
    show()
    await sendCode(user)

    const field = await screen.findByLabelText('Code')
    expect(field.getAttribute('autocomplete')).toBe('one-time-code')
    expect(field.getAttribute('inputmode')).toBe('numeric')
    expect(field.getAttribute('maxlength')).toBe('6')
  })

  it('signs in and lands on the screen for the role', async () => {
    const user = userEvent.setup()
    stub()
    show()
    await sendCode(user)

    await user.type(await screen.findByLabelText('Code'), '123456')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))

    await waitFor(() => {
      expect(screen.getByTestId('location').textContent).toBe('/expert')
    })
    expect(getTokens()).toEqual(TOKENS)
  })

  it('lets a wrong number be corrected without a reload', async () => {
    const user = userEvent.setup()
    stub()
    show()
    await sendCode(user)

    await user.click(await screen.findByRole('button', { name: '← Change number' }))

    expect(screen.getByRole('heading', { name: 'Sign in' })).toBeDefined()
    expect((screen.getByLabelText('Phone (E.164)') as HTMLInputElement).value).toBe(PHONE)
  })

  describe('a code that is not accepted', () => {
    it('says so without claiming to know why', async () => {
      // The API answers wrong, expired, used, exhausted and unknown-number with one identical empty
      // 401, on purpose, so nothing can be probed by trying. The screen must not invent a reason.
      const user = userEvent.setup()
      stub({ verifyStatus: 401 })
      show()
      await sendCode(user)

      await user.type(await screen.findByLabelText('Code'), '000000')
      await user.click(screen.getByRole('button', { name: 'Sign in' }))

      expect(await screen.findByRole('alert')).toBeDefined()
      expect(screen.getByRole('alert').textContent).toContain('That code was not accepted')
    })

    it('names the five-attempt rule once the screen has counted five', async () => {
      // A fact about the rule, not a claim about this code — which is the only version of this the
      // uniform 401 leaves room for. Counted by the screen; the server tells it nothing.
      const user = userEvent.setup()
      stub({ verifyStatus: 401 })
      show()
      await sendCode(user)

      const field = await screen.findByLabelText('Code')
      for (let attempt = 0; attempt < 5; attempt++) {
        await user.clear(field)
        await user.type(field, '00000' + String(attempt))
        await user.click(screen.getByRole('button', { name: 'Sign in' }))
      }

      expect(screen.getByRole('alert').textContent).toContain('can be tried five times')
    })
  })

  describe('the resend throttle', () => {
    it('counts down from the seconds the server sent, not from a guess', async () => {
      // §9 throttles resends and `AuthEndpoints.TooManyRequests` puts the remaining seconds in
      // `Retry-After`. Reading it is what keeps the countdown right across a reload — the browser
      // has no idea when the code was actually sent.
      const user = userEvent.setup()
      stub({ requestStatus: 429, retryAfter: '42' })
      show()
      await sendCode(user)

      expect(await screen.findByText('Resend in 42 s')).toBeDefined()
      expect(screen.queryByRole('button', { name: 'Send a new code' })).toBeNull()
      // A throttled request is not a failure: a code was just sent and is still good.
      expect(screen.queryByRole('alert')).toBeNull()
    })

    it('offers a new code when nothing is throttling it', async () => {
      const user = userEvent.setup()
      stub()
      show()
      await sendCode(user)

      expect(await screen.findByRole('button', { name: 'Send a new code' })).toBeDefined()
      expect(screen.queryByText(/Resend in/)).toBeNull()
    })

    it('shows no countdown at all when the 429 carries no usable header', async () => {
      // Rather than inventing one. A wrong number on screen is worse than no number.
      const user = userEvent.setup()
      stub({ requestStatus: 429, retryAfter: null })
      show()
      await sendCode(user)

      expect(await screen.findByRole('button', { name: 'Send a new code' })).toBeDefined()
      expect(screen.queryByText(/Resend in/)).toBeNull()
    })
  })

  async function sendCode(user: ReturnType<typeof userEvent.setup>) {
    await user.type(screen.getByLabelText('Phone (E.164)'), PHONE)
    await user.click(screen.getByRole('button', { name: 'Send code' }))
  }

  function stub({
    requestStatus = 200,
    retryAfter = null,
    verifyStatus = 200,
  }: { requestStatus?: number; retryAfter?: string | null; verifyStatus?: number } = {}) {
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string) => {
        if (String(url).endsWith('/auth/otp/request')) {
          const headers = retryAfter ? { 'Retry-After': retryAfter } : undefined
          return Promise.resolve(new Response('', { status: requestStatus, headers }))
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

function show() {
  render(
    <MemoryRouter initialEntries={['/login']}>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/expert" element={<p>PLACEHOLDER claims</p>} />
      </Routes>
      <LocationProbe />
    </MemoryRouter>,
  )
}

function LocationProbe() {
  return <span data-testid="location">{useLocation().pathname}</span>
}
