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
  const [rows, setRows] = useState<Row[] | null>(null)
  const [error, setError] = useState('')
  const [version, setVersion] = useState(0)
  /** The row awaiting confirmation. Null means no dialog — see `ConfirmDialog`. */
  const [confirming, setConfirming] = useState<Row | null>(null)
  const [notice, setNotice] = useState('')

  useEffect(() => {
    if (!kind) return
    let cancelled = false
    // No `setRows(null)` here — `react-hooks/set-state-in-effect` forbids it, and it would be the
    // wrong behaviour anyway: the initial `null` covers the first load, and a refetch after a
    // deactivate should keep the rows on screen rather than flashing "Loading experts…" over a list
    // the admin is reading.
    api<Row[]>(apiBase(kind))
      .then((loaded) => {
        if (!cancelled) setRows(loaded)
      })
      .catch((e: unknown) => {
        if (cancelled) return
        // A whole sentence with the status inside it, which is `Banner.tsx`'s own stated rule and
        // what every other screen already does. It read `Load failed (500)` until slice 7.2 — a code
        // shown to an administrator who can do nothing with it.
        setError(
          e instanceof ApiError
            ? `The ${kind.label.toLowerCase()} could not be loaded (${e.status}).`
            : `The ${kind.label.toLowerCase()} could not be loaded.`,
        )
        setRows([])
      })
    return () => {
      cancelled = true
    }
  }, [kind, version])

  if (!kind) return <p className="muted">Unknown profile type.</p>

  // **Both of these swallowed their failures until slice 7.2**, and the invite one was the worse
  // of the pair: `setNotice` sits after the `await`, so a rejected request produced no notice, no
  // banner, no console line and no change on screen — an administrator pressed the button and
  // nothing whatsoever happened. A rejected promise from a `void`-ed handler is also an unhandled
  // rejection, which is the shape browser-pass finding 10 spent a session tracking down.
  async function deactivate(row: Row) {
    setConfirming(null)
    setError('')
    setNotice('')
    try {
      await api(`/api/admin/users/${row.id}/deactivate`, { method: 'POST' })
      setVersion((v) => v + 1)
    } catch (e: unknown) {
      setError(
        e instanceof ApiError
          ? `${row.displayName} was not deactivated (${e.status}).`
          : `${row.displayName} was not deactivated.`,
      )
    }
  }

  async function resendInvite(row: Row) {
    setError('')
    setNotice('')
    try {
      await api(`/api/admin/users/${row.id}/invite`, { method: 'POST' })
      // On the page rather than in a `window.alert`: an alert is modal, unstylable, and cannot be
      // asserted without stubbing a global and testing the stub.
      setNotice('Invite sent.')
    } catch (e: unknown) {
      setError(
        e instanceof ApiError
          ? `The invite to ${row.displayName} was not sent (${e.status}).`
          : `The invite to ${row.displayName} was not sent.`,
      )
    }
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
      {/*
        Three states where there was one (slice 7.2). Before the first response `rows` was `[]`, so a
        slow or failing load rendered an empty table with headers — indistinguishable from "there are
        no garages", which is the answer an administrator would act on by creating one that already
        exists.
      */}
      {rows === null ? (
        <p className="muted">Loading {kind.label.toLowerCase()}…</p>
      ) : rows.length === 0 && !error ? (
        <p className="muted">No {kind.label.toLowerCase()} yet. Use New to invite the first one.</p>
      ) : (
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
      )}

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
