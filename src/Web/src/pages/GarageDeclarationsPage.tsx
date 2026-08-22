import { Link } from 'react-router-dom'
import { ApiError } from '../api/client'
import { formatDateTime } from '../api/datetime'
import type { DeclarationListItem } from '../garage/api'
import { STATE_LABELS } from '../garage/labels'
import { useDeclarations } from '../garage/useDeclarations'

/** G1 (design.md §5.2): the garage's declarations, newest first, with their media counts. */
export default function GarageDeclarationsPage() {
  const { data, isPending, error } = useDeclarations()

  return (
    <section>
      <h2>My declarations</h2>
      <p>
        <Link to="/garage/new">New declaration</Link>
      </p>
      <Results data={data} isPending={isPending} error={error} />
    </section>
  )
}

function Results({
  data,
  isPending,
  error,
}: {
  data: DeclarationListItem[] | undefined
  isPending: boolean
  error: Error | null
}) {
  if (isPending) return <p>Loading declarations…</p>
  if (error) {
    return (
      <p role="alert">
        Could not load declarations{error instanceof ApiError ? ` (${error.status})` : ''}.
      </p>
    )
  }

  if (!data || data.length === 0) {
    return <p>No declarations yet.</p>
  }

  return (
    <table border={1} cellPadding={4}>
      <thead>
        <tr>
          <th>Plate</th>
          <th>Insured</th>
          <th>Status</th>
          <th>Claim</th>
          <th>Created</th>
          <th>Submitted</th>
          <th>Decided</th>
          <th>Media</th>
        </tr>
      </thead>
      <tbody>
        {data.map((item) => (
          <tr key={item.id}>
            <td>
              <Link to={`/garage/${item.id}`}>{item.plateNo}</Link>
            </td>
            <td>{item.insuredName ?? '—'}</td>
            <td>{STATE_LABELS[item.state]}</td>
            {/* No visa until an officer approves (§5.2) — an em dash, not a blank. */}
            <td>{item.visaNo ?? '—'}</td>
            <td>{formatDateTime(item.createdAt)}</td>
            <td>{item.submittedAt ? formatDateTime(item.submittedAt) : '—'}</td>
            <td>{item.decidedAt ? formatDateTime(item.decidedAt) : '—'}</td>
            <td>{item.mediaCount}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
