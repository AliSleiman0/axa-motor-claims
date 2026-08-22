import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
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
import { AlertBanner } from '../ui/Banner'
import { Button } from '../ui/Button'
import { TextField } from '../ui/fields'
import { shellFor } from '../ui/roles'

type Stage = 'invitation' | 'code' | 'done' | 'unusable'

/**
 * S1 (design.md §5.4 / §9): the invitation link, the code, and an active account.
 *
 * **Activation and sign-in are one step.** Verifying the code turns the profile active *and* hands
 * back a session — `InviteService.VerifyAndActivate` issues tokens — so there is no separate "now
 * log in", and the last screen says the invitation will not work again because it genuinely will
 * not (`used_at` is stamped in the same transaction).
 *
 * Nothing is collected here: no name, no password, no photograph. The profile already exists — an
 * admin created it (§5.4's A1) — and this only proves the phone is theirs.
 */
export default function InvitePage() {
  const { token: fromUrl } = useParams()
  const navigate = useNavigate()

  const [token, setToken] = useState(fromUrl ?? '')
  const [stage, setStage] = useState<Stage>('invitation')
  const [code, setCode] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [attempts, setAttempts] = useState(0)
  const [role, setRole] = useState<string | null>(null)
  const [wait, startWait] = useCountdown()

  async function sendCode(): Promise<void> {
    setError('')
    setBusy(true)
    try {
      const response = await fetch('/auth/invite/accept', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token: token.trim() }),
      })

      if (response.status === 429) {
        const seconds = retryAfterSeconds(response)
        if (seconds) startWait(seconds)
        setStage('code')
        return
      }

      if (response.status === 404) {
        // Uniform: invalid, expired and already-used are one answer, so that nobody can test
        // invitations to find out which exist.
        setStage('unusable')
        return
      }

      if (!response.ok) {
        setError(`Could not send a code (${response.status}).`)
        return
      }

      setCode('')
      setAttempts(0)
      setStage('code')
    } finally {
      setBusy(false)
    }
  }

  async function activate(event: React.FormEvent) {
    event.preventDefault()
    setError('')
    setBusy(true)
    try {
      const response = await fetch('/auth/invite/verify', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ token: token.trim(), code }),
      })

      if (response.status === 404) {
        // The invitation went while the code was being typed — expired, or used on another device.
        setStage('unusable')
        return
      }

      if (!response.ok) {
        // §9's uniform 401 again: the screen counts its own tries and never claims to know which of
        // wrong, expired, used or exhausted just happened. **The invitation itself is still good** —
        // only the code failed — which is why the way forward here is a new code, not a new invite.
        const next = attempts + 1
        setAttempts(next)
        setError(next >= OTP_MAX_ATTEMPTS ? OTP_EXHAUSTED : OTP_REJECTED)
        return
      }

      setTokens((await response.json()) as TokenPair)
      setRole(currentRole())
      setStage('done')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="app-shell">
      <header className="app-header">
        <span className="app-header__brand">AXA Motor Claims</span>
      </header>
      <main className="app-main app-main--narrow">
        <section className="page">
          {stage === 'invitation' && (
            <Invitation
              token={token}
              setToken={setToken}
              fromUrl={fromUrl !== undefined}
              busy={busy}
              onSend={() => void sendCode()}
            />
          )}

          {stage === 'code' && (
            <>
              <h1 className="page__title">Confirm your number</h1>
              <form className="panel" onSubmit={activate}>
                {/*
                  The number itself cannot be shown: the invitation is the only thing this browser
                  holds, and the API deliberately returns nothing identifying before activation.
                  A masked number would need a new endpoint and a decision about what it reveals.
                */}
                <p className="muted">
                  AXA has texted a six-digit code to the mobile number on your profile. It is valid
                  for five minutes.
                </p>
                <TextField
                  id="invite-code"
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
                  <Button variant="primary" type="submit" disabled={busy}>
                    {busy ? 'Activating…' : 'Activate my account'}
                  </Button>
                  {wait > 0 ? (
                    <span className="muted">Resend in {wait} s</span>
                  ) : (
                    // A new code does not consume the invitation — only activation does.
                    <Button onClick={() => void sendCode()} disabled={busy}>
                      {busy ? 'Sending…' : 'Send a new code'}
                    </Button>
                  )}
                </div>
              </form>
            </>
          )}

          {stage === 'done' && (
            <>
              <h1 className="page__title">You are signed in</h1>
              <div className="panel">
                <p>
                  Your account is active. From now on, sign in with your mobile number and a fresh
                  code — this invitation will not work again.
                </p>
                <div className="actions">
                  <Button variant="primary" onClick={() => navigate(homePathFor(role))}>
                    Go to {shellFor(role).home}
                  </Button>
                </div>
              </div>
            </>
          )}

          {stage === 'unusable' && (
            <>
              <h1 className="page__title">This invitation cannot be used</h1>
              <div className="panel">
                {/*
                  One screen for three causes — expired, already used, never valid — because the
                  server answers all three identically on purpose. "May" rather than picking one, and
                  the way out is a person rather than a retry: an admin can re-issue while the
                  account is still invited.
                */}
                <p>
                  It may have expired, or it may already have been used. Ask the person at AXA who
                  set up your profile to send a new one.
                </p>
                <div className="actions">
                  {/* An already-used invitation usually means the account is active, so the person
                      is not stuck — they just need the other door. */}
                  <Link className="btn btn--secondary" to="/login">
                    Go to sign in
                  </Link>
                </div>
              </div>
            </>
          )}

          {error && <AlertBanner>{error}</AlertBanner>}
        </section>
      </main>
    </div>
  )
}

function Invitation({
  token,
  setToken,
  fromUrl,
  busy,
  onSend,
}: {
  token: string
  setToken: (next: string) => void
  /** True when the token arrived in the link — then it is shown, not asked for. */
  fromUrl: boolean
  busy: boolean
  onSend: () => void
}) {
  return (
    <>
      <h1 className="page__title">You have been invited</h1>
      <div className="panel">
        <p>
          An AXA administrator has set up a profile for you. Confirm below and AXA texts a six-digit
          code to the mobile number on that profile.
        </p>

        {fromUrl ? (
          <>
            <p className="label">This invitation</p>
            <p className="mono" style={{ wordBreak: 'break-all' }}>
              {token}
            </p>
            <p className="caption">
              Valid for seven days from the day it was sent, and usable once.
            </p>
          </>
        ) : (
          // The paste path. The SMS carries a link now, but carriers strip URLs and somebody may be
          // reading the text on a handset while registering on a laptop.
          <TextField
            id="invite-token"
            label="Invitation"
            value={token}
            onChange={(e) => setToken(e.target.value)}
            className="field__control--mono"
            hint="Long, and case-sensitive — paste it rather than typing it."
            required
          />
        )}

        <div className="actions">
          <Button variant="primary" onClick={onSend} disabled={busy || token.trim().length === 0}>
            {busy ? 'Sending…' : 'Send my code'}
          </Button>
        </div>
      </div>
    </>
  )
}
