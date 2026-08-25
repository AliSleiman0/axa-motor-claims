import { Link } from 'react-router-dom'
import { ApiError } from '../api/client'
import { formatDateTime } from '../api/datetime'
import type { BrokerRequestListItem } from '../broker/api'
import { BROKER_STATE_LABELS } from '../broker/labels'
import { useBrokerAction, useRequests } from '../broker/useBroker'
import { AlertBanner } from '../ui/Banner'
import { Button } from '../ui/Button'
import { StatusChip } from '../ui/StatusChip'
import { BROKER_TONES } from '../ui/tones'
import { Worklist } from '../ui/Worklist'

/**
 * B1 (design.md §5.3) — every request this broker owns, both options, newest first.
 *
 * The rule the artboard states and the screen has to keep: **an Option 2 request exists from the
 * moment the link is issued**, before the customer has typed anything, so three of the seven states
 * legitimately show an em dash for the name and the type. The row is real and the fields are empty.
 */
export default function BrokerRequestsPage() {
  const { data, isPending, error } = useRequests()

  return (
    <section className="page">
      <div className="page__head">
        <h2 className="page__title">Requests</h2>
        <div className="actions">
          <Link className="btn btn--secondary" to="/broker/link">
            Send customer link
          </Link>
          <Link className="btn btn--primary" to="/broker/new">
            New request
          </Link>
        </div>
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
  data: BrokerRequestListItem[] | undefined
  isPending: boolean
  error: Error | null
}) {
  if (isPending) return <p className="muted">Loading requests…</p>
  if (error) {
    return (
      <AlertBanner>
        Could not load your requests{error instanceof ApiError ? ` (${error.status})` : ''}.
      </AlertBanner>
    )
  }

  if (!data || data.length === 0) {
    return <p className="muted">You have not created any requests yet.</p>
  }

  return (
    <Worklist headers={['Insured name', 'Insurance type', 'Option', 'State', 'Created', '']}>
      {/* The server orders newest-first; the screen does not re-sort. */}
      {data.map((request) => (
        <tr key={request.id}>
          {/*
            The name is a link on **every** row (week-5 browser-pass finding 6, fixed in 6.1). One
            action per row is right about actions, but it left a sent request inert — and `/broker/{id}`
            is the only place the routed recipient is shown, which is the question B4's own note says
            this screen exists to answer. O1 does the same with the plate.
          */}
          <td>
            <Link to={`/broker/${request.id}`}>{request.insuredName ?? '—'}</Link>
          </td>
          <td>{request.insuranceType ?? '—'}</td>
          <td className="mono">{request.option}</td>
          <td>
            <StatusChip
              label={BROKER_STATE_LABELS[request.state]}
              // Explicit, because `StatusChip`'s own lookup is keyed on the label and
              // `DECLARATION_TONES` already owns the word "Draft".
              tone={BROKER_TONES[BROKER_STATE_LABELS[request.state]]}
              size="sm"
            />
          </td>
          <td>{formatDateTime(request.createdAt)}</td>
          <td>
            <RowAction request={request} />
          </td>
        </tr>
      ))}
    </Worklist>
  )
}

/**
 * One action per row, **and only where there is one** (the artboard's rule): the rows in between are
 * waiting on somebody else and offer nothing, which is information in itself.
 */
function RowAction({ request }: { request: BrokerRequestListItem }) {
  // A submitted request whose email never left. §5.3 commits the state before the send precisely so
  // this case is visible rather than lost, and this is the button that makes it actionable.
  if (request.state === 'submitted' && request.emailedAt === null) {
    return <Resend requestId={request.id} />
  }

  if (request.state === 'draft') {
    return (
      <Link className="btn btn--secondary" to={`/broker/${request.id}`}>
        Continue
      </Link>
    )
  }

  if (request.state === 'ready_to_send') {
    return (
      <Link className="btn btn--primary" to={`/broker/${request.id}`}>
        Review
      </Link>
    )
  }

  if (request.state === 'expired') {
    return (
      <Link className="btn btn--secondary" to="/broker/link">
        Reissue
      </Link>
    )
  }

  return null
}

function Resend({ requestId }: { requestId: string }) {
  const resend = useBrokerAction(requestId, 'resend')

  // A column, not inline siblings (week-5 finding 4, fixed in 6.1). A `<td>` is not a flex container,
  // so the caption wrapped *around* the button — "Resend  Email" then "not yet sent" underneath — on
  // the one row where something has actually gone wrong.
  return (
    <div className="cell-stack">
      <Button variant="secondary" onClick={resend.run} disabled={resend.pending}>
        {resend.pending ? 'Sending…' : 'Resend'}
      </Button>
      <span className="caption">Email not yet sent</span>
      {resend.failed ? <AlertBanner>{resend.failed}</AlertBanner> : null}
    </div>
  )
}
