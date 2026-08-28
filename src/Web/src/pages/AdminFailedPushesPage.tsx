import { useState } from 'react'
import {
  useOutboxList,
  useRetryAllPushes,
  useRetryPush,
  type OutboxRow,
} from '../admin/outbox'
import { ApiError } from '../api/client'
import { formatDateTime, toUtcDate } from '../api/datetime'
import { AlertBanner } from '../ui/Banner'
import { Button } from '../ui/Button'
import { StatusChip } from '../ui/StatusChip'
import { Worklist } from '../ui/Worklist'

const HEADERS = [
  'Operation',
  'Claim / visa',
  'Attempts',
  'Last error',
  'Created',
  'Last tried',
  '',
]

/**
 * A2 — the failed-push queue (design.md §5.4, the pass-3 `A2Queue` and `A2Empty` artboards).
 *
 * This is the screen that answers "where is that photograph", which is the question the whole project
 * exists to stop being asked. Everything on it is a document, an arrival or a decision that AXA does
 * not have.
 *
 * **Two kinds of row, one screen.** Red is `failed`: given up on, and it will sit there for ever
 * until somebody presses Retry, and red again for an **overdue** `pending` row, which is a queue
 * nothing is draining (slice 7.2) — a different cause with the same shape: it will not clear itself.
 * Amber is `pending` with its next attempt more than one poll away —
 * it has not failed, it is just taking a long time, and it is here so nobody has to wonder.
 * `processing` rows are never listed: they are being pushed right now, and if their worker dies the
 * lease returns them by itself.
 */
export default function AdminFailedPushesPage() {
  const { data, isPending, error, dataUpdatedAt } = useOutboxList()
  const retryAll = useRetryAllPushes()
  const [failure, setFailure] = useState<string | null>(null)

  const rows = data ?? []

  return (
    <section className="page">
      <div className="page__head">
        <h2 className="page__title">Failed pushes</h2>
        {rows.length > 0 ? (
          <div className="actions">
            <Button
              variant="primary"
              disabled={retryAll.isPending}
              onClick={() => {
                // No confirmation dialog, by the artboard's own argument: every push carries a stable
                // clientRef, so re-sending one is meant to be a no-op at NEXT3's end (#32). An admin
                // looking at this screen during an outage should not have to answer a question first.
                setFailure(null)
                retryAll.mutate(undefined, {
                  onError: (retryError) => setFailure(describeRetry(retryError)),
                })
              }}
            >
              {retryAll.isPending ? 'Retrying…' : 'Retry all'}
            </Button>
          </div>
        ) : null}
      </div>

      <p className="muted">
        Everything the app has tried and failed to file in NEXT3. Each row is a document, an arrival
        or a decision that AXA does not have — nothing is lost, but nothing arrives until somebody
        acts.
      </p>

      {failure ? <AlertBanner>{failure}</AlertBanner> : null}

      <Results
        rows={rows}
        isPending={isPending}
        error={error}
        checkedAt={dataUpdatedAt}
        onFailure={setFailure}
      />
    </section>
  )
}

function Results({
  rows,
  isPending,
  error,
  checkedAt,
  onFailure,
}: {
  rows: OutboxRow[]
  isPending: boolean
  error: unknown
  checkedAt: number
  onFailure: (message: string) => void
}) {
  if (isPending) return <p className="muted">Loading the queue…</p>

  if (error) {
    return (
      <AlertBanner>
        Could not load the queue{error instanceof ApiError ? ` (${error.status})` : ''}.
      </AlertBanner>
    )
  }

  if (rows.length === 0) return <NothingFailed checkedAt={checkedAt} />

  return (
    <Worklist headers={HEADERS}>
      {rows.map((row) => (
        <QueueRow key={row.id} row={row} onFailure={onFailure} />
      ))}
    </Worklist>
  )
}

/**
 * Empty is the normal state, so it is drawn as reassurance rather than as a blank table — and the
 * timestamp is the point of it. An admin has to be able to tell "nothing has failed" from "this page
 * has not loaded", and on a monitoring screen that distinction is the whole value of an empty state.
 */
function NothingFailed({ checkedAt }: { checkedAt: number }) {
  return (
    <div className="panel empty-state">
      <span className="empty-state__mark" aria-hidden="true">
        <svg width="24" height="24" viewBox="0 0 24 24" fill="none">
          <path
            d="M5 12.5l4.5 4.5L19 7.5"
            stroke="currentColor"
            strokeWidth="2.5"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
      </span>
      <p className="empty-state__title">Nothing failed.</p>
      <p className="muted">
        Everything the app has sent to NEXT3 has arrived. This screen fills up only when NEXT3 refuses
        something or stays unreachable long enough for the retries to run out.
      </p>
      <p className="caption">
        {/*
          `dataUpdatedAt` is epoch milliseconds, so it is *not* `formatDateTime` — that helper appends
          a `Z` to the bare timestamps SQL Server hands back, and would be the wrong tool on a number.
        */}
        Last checked {new Date(checkedAt).toLocaleString()} · the queue drains on its own every thirty
        seconds.
      </p>
    </div>
  )
}

function QueueRow({ row, onFailure }: { row: OutboxRow; onFailure: (message: string) => void }) {
  const retry = useRetryPush()
  const failed = row.status === 'failed'

  // **Three states on this screen, not two (slice 7.2, found in the browser pass).** The server now
  // lists a `pending` row for either of two opposite reasons — its next attempt is far *ahead* (it
  // is working through a backoff and will clear itself) or far *behind* (nothing is claiming it, so
  // it will not). Rendered as one "Still trying / next attempt due now", the second read as
  // reassurance on exactly the row that needs somebody to act, which is the failure A2 exists to
  // prevent one level down.
  //
  // Derived from the row rather than from a second copy of `Outbox:PollSeconds`: the server only
  // lists a pending row when it is outside the window either way, so a listed pending row whose
  // moment has passed is overdue by construction. No threshold is duplicated here.
  //
  // Behind a function for `describeWait`'s reason, which is also `react-hooks/purity`'s: reading the
  // clock in a component body is flagged, and legitimately — the answer changes between renders.
  // Here that is the intended behaviour and the page refetches on a timer anyway, so the same shape
  // the wait caption has used since 6.2 is the right one.
  const overdue = !failed && isOverdue(row.nextRetryAt)

  return (
    <tr>
      {/* Raw and mono, both of them: an admin reads these two out to a developer. */}
      <td className="mono">
        {row.operation}
        <div>
          {failed ? (
            <StatusChip label="Failed" tone="danger" size="sm" />
          ) : overdue ? (
            <StatusChip label="Not moving" tone="danger" size="sm" />
          ) : (
            <StatusChip label="Still trying" tone="warn" size="sm" />
          )}
        </div>
        {failed ? null : (
          <span className="caption">
            {overdue ? 'overdue — nothing is picking it up' : describeWait(row.nextRetryAt)}
          </span>
        )}
      </td>
      <td className="mono">{row.visaNo}</td>
      <td className="mono">{row.attempts}</td>
      <td>{row.lastError ?? '—'}</td>
      <td>{formatDateTime(row.createdAt)}</td>
      <td>{row.lastAttemptAt ? formatDateTime(row.lastAttemptAt) : '—'}</td>
      <td>
        <Button
          disabled={retry.isPending}
          onClick={() =>
            retry.mutate(row.id, { onError: (error) => onFailure(describeRetry(error)) })
          }
        >
          {/*
            The same endpoint either way. The label differs because the two rows mean different
            things: one is being resurrected, the other is being brought forward.
          */}
          {retry.isPending ? 'Retrying…' : failed || overdue ? 'Retry' : 'Retry now'}
        </Button>
      </td>
    </tr>
  )
}

/**
 * "next attempt in ~2 h". Through `toUtcDate`, never `new Date(iso)` — the API hands back bare
 * timestamps, which are UTC, and reading one as local time would put every estimate out by hours.
 *
 * Local to this page rather than in `api/datetime.ts`: it is the only caller, and lifting it before a
 * second one exists would be guessing at what the second one needs.
 */
function isOverdue(nextRetryAt: string): boolean {
  return toUtcDate(nextRetryAt).getTime() <= Date.now()
}

function describeWait(nextRetryAt: string): string {
  const seconds = Math.round((toUtcDate(nextRetryAt).getTime() - Date.now()) / 1000)
  if (seconds <= 60) return 'next attempt due now'

  const minutes = Math.round(seconds / 60)
  if (minutes < 90) return `next attempt in ~${minutes} min`

  return `next attempt in ~${Math.round(minutes / 60)} h`
}

const EXPLANATIONS: Record<string, string> = {
  // The server answers the same 409 for an unknown id, a row already sent, and a row being pushed
  // right now — deliberately, because all three mean the same thing to the person pressing the
  // button, and none of them is worth a different sentence.
  not_retryable: 'That push is not waiting any more — it has either been sent or is being sent now.',
}

function describeRetry(error: unknown): string {
  if (error instanceof ApiError) {
    for (const [code, sentence] of Object.entries(EXPLANATIONS)) {
      if (error.body.includes(code)) return sentence
    }
    return `Not retried (${error.status}). Check the connection and try again.`
  }

  return 'Not retried. Check the connection and try again.'
}
