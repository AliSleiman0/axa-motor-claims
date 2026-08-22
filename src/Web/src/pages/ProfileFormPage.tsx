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

  useEffect(() => {
    if (!kind || isNew) return
    void api<Record<string, unknown>>(`${apiBase(kind)}/${id}`).then((detail) => {
      const loaded: Record<string, string> = {
        phone: String(detail.phone ?? ''),
        displayName: String(detail.displayName ?? ''),
      }
      for (const field of kind.fields) loaded[field.name] = String(detail[field.name] ?? '')
      setValues(loaded)
    })
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
