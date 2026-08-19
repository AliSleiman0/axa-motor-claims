import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import { apiBase, findKind } from '../admin/kinds'

type Row = { id: string; phone: string; displayName: string; status: string } & Record<string, unknown>

export default function ProfileListPage() {
  const { kind: slug } = useParams()
  const kind = findKind(slug)
  const [rows, setRows] = useState<Row[]>([])
  const [error, setError] = useState('')
  const [version, setVersion] = useState(0)

  useEffect(() => {
    if (!kind) return
    let cancelled = false
    api<Row[]>(apiBase(kind))
      .then((loaded) => {
        if (!cancelled) setRows(loaded)
      })
      .catch((e: unknown) => {
        if (!cancelled) setError(e instanceof ApiError ? `Load failed (${e.status})` : 'Load failed')
      })
    return () => {
      cancelled = true
    }
  }, [kind, version])

  if (!kind) return <p>Unknown profile type.</p>

  async function deactivate(row: Row) {
    if (!window.confirm(`Deactivate ${row.displayName}? They will no longer be able to sign in.`)) return
    await api(`/api/admin/users/${row.id}/deactivate`, { method: 'POST' })
    setVersion((v) => v + 1)
  }

  async function resendInvite(row: Row) {
    await api(`/api/admin/users/${row.id}/invite`, { method: 'POST' })
    window.alert('Invite sent.')
  }

  return (
    <section>
      <h2>{kind.label}</h2>
      {error && <p role="alert">{error}</p>}
      <p>
        <Link to={`/admin/${kind.slug}/new`}>New</Link>
      </p>
      <table border={1} cellPadding={4}>
        <thead>
          <tr>
            <th>Phone</th>
            <th>Name</th>
            <th>Status</th>
            {kind.fields.map((f) => (
              <th key={f.name}>{f.label}</th>
            ))}
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.id}>
              <td>{row.phone}</td>
              <td>{row.displayName}</td>
              <td>{row.status}</td>
              {kind.fields.map((f) => (
                <td key={f.name}>{String(row[f.name] ?? '')}</td>
              ))}
              <td>
                <Link to={`/admin/${kind.slug}/${row.id}`}>Edit</Link>{' '}
                {row.status === 'invited' && (
                  <button type="button" onClick={() => void resendInvite(row)}>
                    Re-send invite
                  </button>
                )}{' '}
                {row.status !== 'inactive' && (
                  <button type="button" onClick={() => void deactivate(row)}>
                    Deactivate
                  </button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}
