import { Link } from 'react-router-dom'
import { ApiError } from '../api/client'
import { formatDateTime } from '../api/datetime'
import type { OfficerDeclarationListItem } from '../officer/api'
import { useInbox } from '../officer/useOfficer'

/**
 * O1 (design.md §5.2): every garage's submitted declarations.
 *
 * Deliberately not scoped to this officer — §5.2 is explicit that there is "no per-officer assignment
 * — any officer may pick it up; the BRD defines no queueing and we do not invent one".
 */
export default function OfficerInboxPage() {
  const { data, isPending, error } = useInbox()

  return (
    <section>
      <h2>Declarations to review</h2>
      <Results data={data} isPending={isPending} error={error} />
    </section>
  )
}

function Results({
  data,
  isPending,
  error,
}: {
  data: OfficerDeclarationListItem[] | undefined
  isPending: boolean
  error: Error | null
}) {
  if (isPending) return <p>Loading declarations…</p>
  if (error) {
    return (
      <p role="alert">
        Could not load the inbox{error instanceof ApiError ? ` (${error.status})` : ''}.
      </p>
    )
  }

  if (!data || data.length === 0) {
    return <p>Nothing is waiting for review.</p>
  }

  return (
    <table border={1} cellPadding={4}>
      <thead>
        <tr>
          <th>Plate</th>
          <th>Insured</th>
          <th>Garage</th>
          <th>Contact</th>
          <th>Submitted</th>
          <th>Media</th>
        </tr>
      </thead>
      <tbody>
        {/*
          The server orders oldest-first, because an officer works a queue and the declaration that
          has waited longest is the one to pick up. The screen does not re-sort: two orderings that
          can disagree is a worse bug than either ordering.
        */}
        {data.map((item) => (
          <tr key={item.id}>
            <td>
              <Link to={`/officer/${item.id}`}>{item.plateNo}</Link>
            </td>
            <td>{item.insuredName ?? '—'}</td>
            <td>{item.garageName ?? '—'}</td>
            <td>{item.garagePhone ?? item.garageEmail ?? '—'}</td>
            <td>{item.submittedAt ? formatDateTime(item.submittedAt) : '—'}</td>
            <td>{item.mediaCount}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
