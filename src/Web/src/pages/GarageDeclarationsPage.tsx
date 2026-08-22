import { Link } from 'react-router-dom'
import { ApiError } from '../api/client'
import { formatDateTime } from '../api/datetime'
import type { DeclarationListItem } from '../garage/api'
import { STATE_LABELS } from '../garage/labels'
import { useDeclarations } from '../garage/useDeclarations'
import { AlertBanner } from '../ui/Banner'
import { StatusChip } from '../ui/StatusChip'
import { WorklistCard } from '../ui/Worklist'

/** G1 (design.md §5.2): the garage's declarations, newest first, with their media counts. */
export default function GarageDeclarationsPage() {
  const { data, isPending, error } = useDeclarations()

  return (
    <section className="page">
      <div className="page__head">
        <h2 className="page__title">My declarations</h2>
        <Link className="btn btn--primary" to="/garage/new">
          New declaration
        </Link>
      </div>
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
  if (isPending) return <p className="muted">Loading declarations…</p>
  if (error) {
    return (
      <AlertBanner>
        Could not load declarations{error instanceof ApiError ? ` (${error.status})` : ''}.
      </AlertBanner>
    )
  }

  if (!data || data.length === 0) {
    // §5.2's survey path in one sentence: this is the garage-initiated route, and a garage seeing an
    // empty worklist on day one should learn when to use it rather than what it does not contain.
    return (
      <p className="muted">
        No declarations yet. Start one when a customer brings a car in instead of calling for an
        expert.
      </p>
    )
  }

  return (
    <div className="worklist-cards">
      {data.map((item) => (
        <WorklistCard
          key={item.id}
          to={`/garage/${item.id}`}
          reference={item.plateNo}
          status={<StatusChip label={STATE_LABELS[item.state]} size="sm" />}
          meta={[
            { label: 'Insured', value: item.insuredName ?? '—' },
            // No visa until an officer approves (§5.2) — an em dash, not a blank.
            { label: 'Claim', value: item.visaNo ?? '—' },
            { label: 'Created', value: formatDateTime(item.createdAt) },
            { label: 'Submitted', value: item.submittedAt ? formatDateTime(item.submittedAt) : '—' },
            { label: 'Decided', value: item.decidedAt ? formatDateTime(item.decidedAt) : '—' },
            /*
              The count is declaration-wide and **includes the officer's approval image**, so a
              garage that uploaded two documents reads three once the claim is approved. Decided in
              slice 4.3 and recorded in scope-decisions: the approval image is the declaration's
              media, filed under the same visa in the same folder, and excluding it would put a §7.1
              bucket rule outside the table §7.1 is encoded in.
            */
            { label: 'Media', value: item.mediaCount },
          ]}
        />
      ))}
    </div>
  )
}
