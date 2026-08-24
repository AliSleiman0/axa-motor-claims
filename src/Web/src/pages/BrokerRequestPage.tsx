import { Link, useParams } from 'react-router-dom'
import { formatDateTime } from '../api/datetime'
import type { BrokerDocument, BrokerRequestDetail } from '../broker/api'
import { requestDocumentsPath } from '../broker/api'
import { BROKER_STATE_LABELS } from '../broker/labels'
import {
  useBrokerAction,
  useRefreshAfterCapture,
  useRequest,
  useRequestDocuments,
} from '../broker/useBroker'
import { CapturePanel } from '../media/CapturePanel'
import { useMediaConfig } from '../media/useMediaConfig'
import { AlertBanner, StatusBanner } from '../ui/Banner'
import { Button } from '../ui/Button'
import { DetailTable } from '../ui/DetailTable'
import { DocumentRow } from '../ui/DocumentRow'
import { StatusChip } from '../ui/StatusChip'
import { BROKER_TONES } from '../ui/tones'

/**
 * B2's second half (design.md §5.3): one Option 1 request, its documents, and the send.
 *
 * The branching is §5.3's state machine seen from the screen: a `draft` collects documents and offers
 * Submit; everything after it is an outcome, and **every outcome names the recipient address**. That
 * is the artboard's rule and a deliberate one — "the address is shown, not hidden behind 'sent
 * successfully'. If the routing table is wrong, this is the screen where somebody notices."
 */
export default function BrokerRequestPage() {
  const { id } = useParams()
  if (!id) return <AlertBanner>No request selected.</AlertBanner>
  return <RequestView requestId={id} />
}

function RequestView({ requestId }: { requestId: string }) {
  const { data, isPending, error } = useRequest(requestId)

  if (isPending) return <p className="muted">Loading request…</p>
  if (error || !data) return <AlertBanner>This request could not be loaded.</AlertBanner>

  return (
    <section className="page">
      <Link to="/broker">← Requests</Link>
      <div className="page__head">
        <h2 className="page__title">{data.insuredName ?? 'Request'}</h2>
        <StatusChip
          label={BROKER_STATE_LABELS[data.state]}
          tone={BROKER_TONES[BROKER_STATE_LABELS[data.state]]}
        />
      </div>

      <Details request={data} />

      {data.state === 'draft' ? (
        <DraftPanel requestId={requestId} />
      ) : (
        <OutcomePanel request={data} />
      )}

      <Documents requestId={requestId} />
    </section>
  )
}

function Details({ request }: { request: BrokerRequestDetail }) {
  return (
    <section className="panel">
      <h3 className="panel__title">Details</h3>
      <DetailTable
        rows={[
          { label: 'Insured name', value: request.insuredName },
          { label: 'Insurance type', value: request.insuranceType },
          { label: 'Address', value: request.insuredAddress },
          // No currency symbol anywhere — none is specified, and #47 asks which (see `MoneyField`).
          { label: 'Car value', value: amount(request.carValue), mono: true },
          { label: 'Estimated premium', value: amount(request.estimatedPremium), mono: true },
          { label: 'Effective date', value: request.effectiveDate, mono: true },
        ]}
      />
    </section>
  )
}

/** The one state where documents may be attached — after the submit the email has already been built. */
function DraftPanel({ requestId }: { requestId: string }) {
  const { data: config } = useMediaConfig()
  const refresh = useRefreshAfterCapture(requestId)
  const submit = useBrokerAction(requestId, 'submit')

  return (
    <>
      <CapturePanel
        path={requestDocumentsPath(requestId)}
        bucket="broker_document"
        label="Documents"
        config={config}
        onUploaded={refresh}
        qualifier="supporting document"
      />

      <section className="panel">
        {submit.failed ? <AlertBanner>{submit.failed}</AlertBanner> : null}
        <Button variant="primary" onClick={submit.run} disabled={submit.pending}>
          {submit.pending ? 'Sending…' : 'Submit'}
        </Button>
        <span className="caption">
          This sends the details and every document above to AXA, in one email.
        </span>
      </section>
    </>
  )
}

/**
 * Every state after `draft`. Two of them, and the difference matters more than it looks:
 * `emailedAt` null on a submitted request means the request is filed and the email is **not** gone —
 * §5.3 commits the state before the send so exactly this can be seen and acted on, rather than a
 * submission being rolled back because a mail server was unreachable.
 */
function OutcomePanel({ request }: { request: BrokerRequestDetail }) {
  const resend = useBrokerAction(request.id, 'resend')
  const unsent = request.emailedAt === null

  return (
    <section className="panel">
      <h3 className="panel__title">{unsent ? 'Not sent yet' : 'Sent to AXA'}</h3>

      {unsent ? (
        <>
          <StatusBanner>
            This request is filed, but the email has not gone yet. Try again — nothing is lost, and
            it will not be sent twice.
          </StatusBanner>
          {resend.failed ? <AlertBanner>{resend.failed}</AlertBanner> : null}
          <Button variant="primary" onClick={resend.run} disabled={resend.pending}>
            {resend.pending ? 'Sending…' : 'Send email'}
          </Button>
        </>
      ) : (
        <>
          <p>
            {request.insuredName ?? 'This request'}’s request has gone to the{' '}
            {request.insuranceType} desk.
          </p>
          <DetailTable
            rows={[
              { label: 'Recipient', value: request.emailRecipient, mono: true },
              {
                label: 'Sent',
                value: request.emailedAt ? formatDateTime(request.emailedAt) : null,
              },
            ]}
          />
        </>
      )}
    </section>
  )
}

/**
 * The attached documents, with **the provenance flag on every row** — "AXA asked for that flag and it
 * is on every row in the system, not only here". No `PushIndicator`: a broker document is
 * `PushTiming.Never`, so there is no push to be in a state about.
 */
function Documents({ requestId }: { requestId: string }) {
  const { data, isPending, error } = useRequestDocuments(requestId)

  if (isPending) return <p className="muted">Loading documents…</p>
  if (error) return <AlertBanner>The documents could not be loaded.</AlertBanner>
  if (!data || data.length === 0) return null

  return (
    <section className="panel">
      <h3 className="panel__title">Documents</h3>
      {data.map((document) => (
        <DocumentRow
          key={document.id}
          name={document.fileName ?? document.bucket}
          bucket={document.bucket}
          indicator={<ProvenanceChip document={document} />}
        />
      ))}
    </section>
  )
}

function ProvenanceChip({ document }: { document: BrokerDocument }) {
  return (
    <StatusChip
      label={document.origin === 'captured' ? 'captured' : 'uploaded'}
      tone={document.origin === 'captured' ? 'accent' : 'neutral'}
      size="sm"
    />
  )
}

function amount(value: number | null): string | null {
  return value === null ? null : value.toFixed(2)
}
