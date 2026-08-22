import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { currentRole, homePathFor } from '../api/session'
import { setTokens, type TokenPair } from '../api/tokens'
import {
  OTP_EXHAUSTED,
  OTP_LENGTH,
  OTP_MAX_ATTEMPTS,
  OTP_REJECTED,
  retryAfterSeconds,
} from '../auth/otp'
import { useCountdown } from '../auth/useCountdown'
import { PushUnsupportedNotice } from '../push/PushUnsupportedNotice'
import { AlertBanner } from '../ui/Banner'
import { Button } from '../ui/Button'
import { PhoneField, TextField } from '../ui/fields'

/**
 * S2 (design.md §9): phone, then a code. Two steps, and the second one names the number it is
 * waiting on — somebody who mistyped a digit needs to see that before they go looking for a text.
 *
 * **Raw `fetch`, not `api<T>()`, and deliberately so.** `api` throws an `ApiError` carrying only the
 * status and the body, and this screen needs a *header*: the resend countdown is read from the
 * `Retry-After` on the 429 (`AuthEndpoints.TooManyRequests`) rather than guessed from the configured
 * value, which is what keeps it right across a reload. `api`'s refresh-and-retry is meaningless here
 * too — there is no session yet to refresh.
 */
export default function LoginPage() {
  const navigate = useNavigate()
  const [phone, setPhone] = useState('')
  const [code, setCode] = useState('')
  const [codeSent, setCodeSent] = useState(false)
  const [error, setError] = useState('')
  const [sending, setSending] = useState(false)
  const [verifying, setVerifying] = useState(false)
  // Counted here, never inferred from the server — see `OTP_MAX_ATTEMPTS`.
  const [attempts, setAttempts] = useState(0)
  const [wait, startWait] = useCountdown()

  async function sendCode(): Promise<void> {
    setError('')
    setSending(true)
    try {
      const response = await fetch('/auth/otp/request', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ phone }),
      })

      if (response.status === 429) {
        const seconds = retryAfterSeconds(response)
        if (seconds) startWait(seconds)
        // Not an error: a code was just sent and is still good. The countdown is the whole message.
        setCodeSent(true)
        return
      }

      if (!response.ok) {
        setError(`Could not request a code (${response.status}).`)
        return
      }

      setCode('')
      setAttempts(0)
      setCodeSent(true)
    } finally {
      setSending(false)
    }
  }

  async function requestCode(event: React.FormEvent) {
    event.preventDefault()
    await sendCode()
  }

  async function verifyCode(event: React.FormEvent) {
    event.preventDefault()
    setError('')
    setVerifying(true)
    try {
      const response = await fetch('/auth/otp/verify', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ phone, code }),
      })

      if (!response.ok) {
        // One uniform 401 covers wrong, expired, used, exhausted and unknown-number, on purpose, so
        // nothing can be probed by trying. The screen therefore says only what it can defend: this
        // code was not accepted, and — once it has counted five of its own tries — that a code only
        // gets five. It never claims to know which of those five things just happened.
        const next = attempts + 1
        setAttempts(next)
        setError(next >= OTP_MAX_ATTEMPTS ? OTP_EXHAUSTED : OTP_REJECTED)
        return
      }

      setTokens((await response.json()) as TokenPair)
      // Read back through currentRole() rather than trusting a local variable: the token just stored
      // is the one the rest of the app will route on.
      navigate(homePathFor(currentRole()))
    } finally {
      setVerifying(false)
    }
  }

  return (
    // No `AppShell`: there is no role to name yet, and a Sign out on a sign-in screen would be its
    // own small joke. The header is the wordmark alone.
    <div className="app-shell">
      <header className="app-header">
        <span className="app-header__brand">AXA Motor Claims</span>
      </header>
      <main className="app-main app-main--narrow">
        <section className="page">
          {!codeSent ? (
            <>
              <h1 className="page__title">Sign in</h1>
              <form className="panel" onSubmit={requestCode}>
                <PhoneField
                  id="login-phone"
                  label="Phone (E.164)"
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  placeholder="+999..."
                  required
                />
                <div className="actions">
                  <Button variant="primary" type="submit" disabled={sending}>
                    {sending ? 'Sending…' : 'Send code'}
                  </Button>
                </div>
              </form>
            </>
          ) : (
            <>
              <h1 className="page__title">Enter the code</h1>
              <form className="panel" onSubmit={verifyCode}>
                <p className="muted">
                  Sent to <span className="mono">{phone}</span>. It is valid for five minutes.
                </p>
                {/*
                  **One field, not six boxes.** A pasted or autofilled code has to land somewhere,
                  and `one-time-code` is what lets a phone offer the code straight from the
                  notification without the person leaving the app. `inputMode` gives a numeric
                  keypad while the field stays a plain text input, so autofill still works.
                */}
                <TextField
                  id="login-code"
                  label="Code"
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  maxLength={OTP_LENGTH}
                  className="field__control--mono"
                  required
                />
                <div className="actions">
                  <Button variant="primary" type="submit" disabled={verifying}>
                    {verifying ? 'Signing in…' : 'Sign in'}
                  </Button>
                  {wait > 0 ? (
                    // The server's own number, not ours. See `retryAfterSeconds`.
                    <span className="muted">Resend in {wait} s</span>
                  ) : (
                    <Button onClick={() => void sendCode()} disabled={sending}>
                      {sending ? 'Sending…' : 'Send a new code'}
                    </Button>
                  )}
                </div>
                <Button
                  variant="link"
                  onClick={() => {
                    setCodeSent(false)
                    setError('')
                  }}
                >
                  ← Change number
                </Button>
              </form>
            </>
          )}
          {error && <AlertBanner>{error}</AlertBanner>}
          {/* Before sign-in on purpose — an expert who will never get popups should learn it here
              rather than at a crash site. Renders nothing when the browser can do push. */}
          <PushUnsupportedNotice />
        </section>
      </main>
    </div>
  )
}
