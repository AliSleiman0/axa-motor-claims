import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import { apiBase, findKind } from '../admin/kinds'

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

  if (!kind) return <p>Unknown profile type.</p>

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
      setError(e instanceof ApiError ? `Save failed (${e.status}): ${e.body}` : 'Save failed')
    }
  }

  return (
    <section>
      <h2>
        {isNew ? 'New' : 'Edit'} — {kind.label}
      </h2>
      <form onSubmit={submit}>
        <p>
          <label>
            Phone (E.164){' '}
            <input
              value={values.phone ?? ''}
              onChange={(e) => set('phone', e.target.value)}
              disabled={!isNew}
              required={isNew}
              placeholder="+999..."
            />
          </label>
        </p>
        <p>
          <label>
            Display name{' '}
            <input value={values.displayName ?? ''} onChange={(e) => set('displayName', e.target.value)} required />
          </label>
        </p>
        {kind.fields.map((field) => (
          <p key={field.name}>
            <label>
              {field.label}{' '}
              <input
                value={values[field.name] ?? ''}
                onChange={(e) => set(field.name, e.target.value)}
                required={field.required}
              />
            </label>
          </p>
        ))}
        <button type="submit">{isNew ? 'Create and send invite' : 'Save'}</button>
      </form>
      {error && <p role="alert">{error}</p>}
    </section>
  )
}
