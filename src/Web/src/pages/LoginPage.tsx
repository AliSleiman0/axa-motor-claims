import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { setTokens, type TokenPair } from '../api/tokens'

export default function LoginPage() {
  const navigate = useNavigate()
  const [phone, setPhone] = useState('')
  const [code, setCode] = useState('')
  const [codeSent, setCodeSent] = useState(false)
  const [error, setError] = useState('')

  async function requestCode(event: React.FormEvent) {
    event.preventDefault()
    setError('')
    const response = await fetch('/auth/otp/request', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ phone }),
    })
    if (!response.ok) {
      setError(`Could not request a code (${response.status})`)
      return
    }
    setCodeSent(true)
  }

  async function verifyCode(event: React.FormEvent) {
    event.preventDefault()
    setError('')
    const response = await fetch('/auth/otp/verify', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ phone, code }),
    })
    if (!response.ok) {
      setError('Code rejected')
      return
    }
    setTokens((await response.json()) as TokenPair)
    navigate('/admin/experts')
  }

  return (
    <main>
      <h1>Sign in</h1>
      {!codeSent ? (
        <form onSubmit={requestCode}>
          <label>
            Phone (E.164){' '}
            <input value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+999..." required />
          </label>{' '}
          <button type="submit">Send code</button>
        </form>
      ) : (
        <form onSubmit={verifyCode}>
          <p>Code sent to {phone} (dev: see API log)</p>
          <label>
            Code <input value={code} onChange={(e) => setCode(e.target.value)} required />
          </label>{' '}
          <button type="submit">Sign in</button>{' '}
          <button type="button" onClick={() => setCodeSent(false)}>
            Back
          </button>
        </form>
      )}
      {error && <p role="alert">{error}</p>}
    </main>
  )
}
