import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import { apiBase, findKind } from '../admin/kinds'
import { describeSaveError } from '../admin/saveErrors'
import { AlertBanner } from '../ui/Banner'
import { Button } from '../ui/Button'
import { PhoneField, TextField } from '../ui/fields'

/** Create ('new') and edit (guid id) from the same §4 field descriptors. Phone is immutable after create. */
export default function ProfileFormPage() {
  const { kind: slug, id } = useParams()
  const kind = findKind(slug)
  const navigate = useNavigate()
  const isNew = id === undefined
  const [values, setValues] = useState<Record<string, string>>({})
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(!isNew)

  // **The uncaught fetch, and the reason this page had no test until slice 7.2 — the two gaps
  // coincided.** A failed load produced an unhandled rejection and an *empty form*: every field
  // blank, the Save button live, and a PUT away from overwriting a real profile with nothing. The
  // cancellation flag is `ProfileListPage`'s, which had one and this did not.
  useEffect(() => {
    if (!kind || isNew) return undefined
    // No `setLoading(true)` here — the initial value is already `!isNew`, and the lint rule
    // (`react-hooks/set-state-in-effect`) forbids a synchronous set in an effect body.
    let cancelled = false
    api<Record<string, unknown>>(`${apiBase(kind)}/${id}`)
      .then((detail) => {
        if (cancelled) return
        const loaded: Record<string, string> = {
          phone: String(detail.phone ?? ''),
          displayName: String(detail.displayName ?? ''),
        }
        for (const field of kind.fields) loaded[field.name] = String(detail[field.name] ?? '')
        setValues(loaded)
        setLoading(false)
      })
      .catch((e: unknown) => {
        if (cancelled) return
        setError(
          e instanceof ApiError
            ? `This profile could not be loaded (${e.status}). Nothing has been changed.`
            : 'This profile could not be loaded. Nothing has been changed.',
        )
        setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [kind, id, isNew])

  if (!kind) return <p className="muted">Unknown profile type.</p>

  function set(name: string, value: string) {
    setValues((v) => ({ ...v, [name]: value }))
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault()
    setError('')
    const payload: Record<string, string | null> = { displayName: values.displayName ?? '' }
    if (isNew) payload.phone = values.phone ?? ''
    for (const field of kind!.fields) payload[field.name] = values[field.name] || null
    try {
      await api(isNew ? apiBase(kind!) : `${apiBase(kind!)}/${id}`, {
        method: isNew ? 'POST' : 'PUT',
        body: JSON.stringify(payload),
      })
      navigate(`/admin/${kind!.slug}`)
    } catch (e) {
      // The three refusals the API actually returns become sentences here; anything else keeps the
      // status. What it never does again is print the raw response body at an administrator.
      setError(e instanceof ApiError ? describeSaveError(e) : 'The profile was not saved.')
    }
  }

  return (
    <section className="page">
      <h2 className="page__title">
        {isNew ? 'New' : 'Edit'} — {kind.label}
      </h2>
      {loading && <p className="muted">Loading profile…</p>}
      <form className="panel" onSubmit={submit}>
        <PhoneField
          id="profile-phone"
          label="Phone (E.164)"
          value={values.phone ?? ''}
          onChange={(e) => set('phone', e.target.value)}
          // §4: the phone is the identity §9 logs in with, so it is locked after creation.
          disabled={!isNew}
          required={isNew}
          placeholder="+999..."
        />
        <TextField
          id="profile-display-name"
          label="Display name"
          value={values.displayName ?? ''}
          onChange={(e) => set('displayName', e.target.value)}
          required
        />
        {kind.fields.map((field) => (
          <TextField
            key={field.name}
            id={`profile-${field.name}`}
            label={field.label}
            value={values[field.name] ?? ''}
            onChange={(e) => set(field.name, e.target.value)}
            required={field.required}
          />
        ))}
        <div className="actions">
          <Button variant="primary" type="submit">
            {isNew ? 'Create and send invite' : 'Save'}
          </Button>
        </div>
      </form>
      {error && <AlertBanner>{error}</AlertBanner>}
    </section>
  )
}
