import { Link } from 'react-router-dom'
import { ApiError } from '../api/client'
import { formatDateTime } from '../api/datetime'
import type { OfficerDeclarationListItem } from '../officer/api'
import { useInbox } from '../officer/useOfficer'
import { AlertBanner } from '../ui/Banner'
import { Worklist } from '../ui/Worklist'

/**
 * O1 (design.md §5.2): every garage's submitted declarations.
 *
 * Deliberately not scoped to this officer — §5.2 is explicit that there is "no per-officer assignment
 * — any officer may pick it up; the BRD defines no queueing and we do not invent one".
 */
export default function OfficerInboxPage() {
  const { data, isPending, error } = useInbox()

  return (
    <section className="page">
      <h2 className="page__title">Declarations to review</h2>
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
  if (isPending) return <p className="muted">Loading declarations…</p>
  if (error) {
    return (
      <AlertBanner>
        Could not load the inbox{error instanceof ApiError ? ` (${error.status})` : ''}.
      </AlertBanner>
    )
  }

  if (!data || data.length === 0) {
    return <p className="muted">Nothing is waiting for review.</p>
  }

  return (
    <Worklist headers={['Plate', 'Insured', 'Garage', 'Contact', 'Submitted', 'Media']}>
      {/*
        The server orders oldest-first, because an officer works a queue and the declaration that
        has waited longest is the one to pick up. The screen does not re-sort: two orderings that
        can disagree is a worse bug than either ordering.
      */}
      {data.map((item) => (
        <tr key={item.id}>
          <td className="mono">
            <Link to={`/officer/${item.id}`}>{item.plateNo}</Link>
          </td>
          <td>{item.insuredName ?? '—'}</td>
          <td>{item.garageName ?? '—'}</td>
          {/* Mobile, then email, then an em dash — "the phone number is the messaging" (pass 3). */}
          <td>{item.garagePhone ?? item.garageEmail ?? '—'}</td>
          <td>{item.submittedAt ? formatDateTime(item.submittedAt) : '—'}</td>
          <td>{item.mediaCount}</td>
        </tr>
      ))}
    </Worklist>
  )
}
