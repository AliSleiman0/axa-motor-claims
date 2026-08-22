import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import { apiBase, findKind } from '../admin/kinds'
import { AlertBanner, StatusBanner } from '../ui/Banner'
import { Button } from '../ui/Button'
import { ConfirmDialog } from '../ui/ConfirmDialog'
import { ProfileStatusChip } from '../ui/StatusChip'
import { Worklist } from '../ui/Worklist'

type Row = { id: string; phone: string; displayName: string; status: string } & Record<string, unknown>

export default function ProfileListPage() {
  const { kind: slug } = useParams()
  const kind = findKind(slug)
  const [rows, setRows] = useState<Row[]>([])
  const [error, setError] = useState('')
  const [version, setVersion] = useState(0)
  /** The row awaiting confirmation. Null means no dialog — see `ConfirmDialog`. */
  const [confirming, setConfirming] = useState<Row | null>(null)
  const [notice, setNotice] = useState('')

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

  if (!kind) return <p className="muted">Unknown profile type.</p>

  async function deactivate(row: Row) {
    setConfirming(null)
    await api(`/api/admin/users/${row.id}/deactivate`, { method: 'POST' })
    setVersion((v) => v + 1)
  }

  async function resendInvite(row: Row) {
    await api(`/api/admin/users/${row.id}/invite`, { method: 'POST' })
    // On the page rather than in a `window.alert`: an alert is modal, unstylable, and cannot be
    // asserted without stubbing a global and testing the stub.
    setNotice('Invite sent.')
  }

  return (
    <section className="page">
      <div className="page__head">
        <h2 className="page__title">{kind.label}</h2>
        <Link className="btn btn--primary" to={`/admin/${kind.slug}/new`}>
          New
        </Link>
      </div>
      {error && <AlertBanner>{error}</AlertBanner>}
      {notice && <StatusBanner>{notice}</StatusBanner>}
      <Worklist
        headers={['Phone', 'Name', 'Status', ...kind.fields.map((f) => f.label), 'Actions']}
      >
        {rows.map((row) => (
          /*
            Three states, three row treatments (pass 3). **The inactive row is greyed as well as
            chipped**: an admin scanning a long list should see at a glance who is switched off,
            without reading a column — and the chip alone is a column.
          */
          <tr key={row.id} className={row.status === 'inactive' ? 'worklist__row--muted' : undefined}>
            <td className="mono">{row.phone}</td>
            <td>{row.displayName}</td>
            <td>
              <ProfileStatusChip status={row.status} />
            </td>
            {kind.fields.map((f) => (
              <td key={f.name}>{String(row[f.name] ?? '')}</td>
            ))}
            <td>
              <span className="actions">
                <Link to={`/admin/${kind.slug}/${row.id}`}>Edit</Link>
                {/* Offered only while `invited` — pass 3, and the server would refuse it anyway.
                    Once somebody has signed in there is nothing to re-send. */}
                {row.status === 'invited' && (
                  <Button onClick={() => void resendInvite(row)}>Re-send invite</Button>
                )}
                {row.status !== 'inactive' && (
                  <Button variant="destructive" onClick={() => setConfirming(row)}>
                    Deactivate
                  </Button>
                )}
              </span>
            </td>
          </tr>
        ))}
      </Worklist>

      {/*
        The one action on this screen that asks, because it is the one that cannot be undone: there
        is no reactivate and no delete. Work already in flight is untouched — a claim they were
        assigned stays assigned — which is why the sentence names sign-in and nothing else.
      */}
      <ConfirmDialog
        message={
          confirming
            ? `Deactivate ${confirming.displayName}? They will no longer be able to sign in.`
            : null
        }
        confirmLabel="Deactivate"
        onCancel={() => setConfirming(null)}
        onConfirm={() => {
          if (confirming) void deactivate(confirming)
        }}
      />
    </section>
  )
}
