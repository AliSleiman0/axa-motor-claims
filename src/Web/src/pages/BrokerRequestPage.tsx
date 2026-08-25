import { Link, useParams } from 'react-router-dom'
import { formatDateTime } from '../api/datetime'
import type { BrokerDocument, BrokerRequestDetail } from '../broker/api'
import { requestDocumentContentPath, requestDocumentsPath } from '../broker/api'
import { BROKER_STATE_LABELS } from '../broker/labels'
import {
  useBrokerAction,
  useBrokerConfig,
  useRefreshAfterCapture,
  useRequest,
  useRequestDocuments,
} from '../broker/useBroker'
import { CapturePanel } from '../media/CapturePanel'
import { useDocumentBlobUrl } from '../media/useDocumentBlobUrl'
import { useMediaConfig } from '../media/useMediaConfig'
import { CAR_SHOT_BUCKETS, CAR_SHOT_PANELS } from '../public/carShots'
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
        <>
          <OutcomePanel request={data} />
          <Documents requestId={requestId} />
        </>
      )}
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

/**
 * The one state where documents may be attached — after the submit the email has already been built.
 *
 * **Capture, then the attached list, then Submit** (week-5 browser-pass finding 1, fixed in 6.1). The
 * list used to render *below* the button, so a broker pressed Submit before ever seeing what was
 * attached — and the email is built from exactly that list. Both panels were also headed "Documents";
 * the list is "Attached" now, which is what P1 calls its own.
 */
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

      <Documents requestId={requestId} title="Attached" />

      <section className="panel">
        {submit.failed ? <AlertBanner>{submit.failed}</AlertBanner> : null}
        <Button variant="primary" onClick={submit.run} disabled={submit.pending}>
          {submit.pending ? 'Sending…' : 'Submit'}
        </Button>
        {/*
          "any documents", not "every document" (week-5 finding 5). Submit has no document
          requirement — whether Option 1 should have one is #48, unanswered — so until it is answered
          the caption says what the button actually does rather than promising attachments that may
          not exist. A gate would be inventing a rule the BRD does not state.
        */}
        <span className="caption">
          This sends the details and any documents above to AXA, in one email.
        </span>
      </section>
    </>
  )
}

/**
 * Every state after `draft`, and **it branches on the state, which it did not before slice 5.3.**
 *
 * The week-5 browser pass caught the cost of that: `link_issued` has `emailedAt` null like a failed
 * Option 1 send does, so a request nobody had touched read *"This request is filed, but the email has
 * not gone yet"* over a **Send email** button — which the server then refused with `409
 * not_submitted`, leaving two contradictory sentences on one card. `emailedAt` alone cannot tell
 * "never sent because it failed" from "never sent because there is nothing to send yet", and those
 * are opposite screens.
 */
function OutcomePanel({ request }: { request: BrokerRequestDetail }) {
  switch (request.state) {
    case 'link_issued':
    case 'customer_in_progress':
      return <NothingToReview request={request} />
    case 'expired':
      return <ExpiredLink />
    case 'ready_to_send':
      return <ReviewPanel request={request} />
    default:
      return <SentPanel request={request} />
  }
}

/**
 * B4's "nothing to review yet" (the BrokerStates artboard). **Deliberately does not show what the
 * customer has typed so far**: the submission is one act, and half a form is not information, it is a
 * person mid-sentence. No send control either — there is nothing to send.
 */
function NothingToReview({ request }: { request: BrokerRequestDetail }) {
  const opened = request.state === 'customer_in_progress'

  return (
    <section className="panel">
      <h3 className="panel__title">Nothing to review yet</h3>
      <StatusBanner>
        {opened
          ? 'The customer has opened the link. You will get a notification the moment they send it.'
          : 'The link has been issued. You will get a notification the moment the customer sends it.'}
      </StatusBanner>
      <DetailTable
        rows={[
          { label: 'Sent to', value: request.customerMobile, mono: true },
          {
            label: 'Link expires',
            value: request.linkExpiresAt ? formatDateTime(request.linkExpiresAt) : null,
          },
        ]}
      />
    </section>
  )
}

/** A link that ran out before the customer finished. The way forward is a new one — §9.1: a locked
 * or expired token cannot be reopened, by design. */
function ExpiredLink() {
  return (
    <section className="panel">
      <h3 className="panel__title">This link has run out</h3>
      <StatusBanner>
        The customer did not send it in time. Issue a new link — this one cannot be reopened.
      </StatusBanner>
      <Link className="btn btn--secondary" to="/broker/link">
        Create a new link
      </Link>
    </section>
  )
}

/**
 * B4 (design.md §5.3). The customer has sent it; the broker reads it and releases it to AXA.
 *
 * **Read-only by rule**, and the rule is pass 3's: editing here would put the broker's words in the
 * customer's submission. A wrong submission means a new link, not a correction. The six fields are
 * already above in `Details`, so this panel is the decision and its consequence — one press, no
 * confirmation dialog, because the customer has already committed and the recipient is decided by
 * config rather than by the broker.
 */
function ReviewPanel({ request }: { request: BrokerRequestDetail }) {
  const send = useBrokerAction(request.id, 'send')
  const { data: config } = useBrokerConfig()

  return (
    <>
      <CarPhotosPanel requestId={request.id} />

      <section className="panel">
        <h3 className="panel__title">Send to AXA</h3>
        <p>
          Sent by the customer
          {request.submittedAt ? ` on ${formatDateTime(request.submittedAt)}` : ''}. Read it through,
          then send it to AXA.
        </p>
        <DetailTable
          rows={[
            {
              label: 'Goes to',
              value: request.insuranceType
                ? (config?.emailRouting[request.insuranceType] ?? null)
                : null,
              mono: true,
            },
          ]}
        />
        <span className="caption">
          Chosen by the insurance type, not by you. This sends the details and every document to AXA,
          in one email.
        </span>
        {send.failed ? <AlertBanner>{send.failed}</AlertBanner> : null}
        <Button variant="primary" onClick={send.run} disabled={send.pending}>
          {send.pending ? 'Sending…' : 'Send email'}
        </Button>
      </section>
    </>
  )
}

/**
 * The terminal state for both options — and the one place `emailedAt` is still the right question.
 *
 * Null here means the state committed and the send did not: §5.3 commits before it sends, precisely
 * so this is visible and actionable rather than a submission rolled back because a mail server was
 * unreachable. Resend covers both options since slice 5.3, which is why one panel serves them.
 */
function SentPanel({ request }: { request: BrokerRequestDetail }) {
  const resend = useBrokerAction(request.id, 'resend')
  const unsent = request.emailedAt === null

  return (
    <>
      <SentSummary request={request} resend={resend} unsent={unsent} />
      {/*
        Option 2 only: an Option 1 request has no car photographs and never had, so the panel would
        be five "Not provided" slots saying nothing. Shown *after* the send as well as before it
        because the review is the last time anyone looks at this request, and a screen that dropped
        the photographs the moment the email left would read as a customer who never sent any.
      */}
      {request.option === 2 ? <CarPhotosPanel requestId={request.id} /> : null}
    </>
  )
}

function SentSummary({
  request,
  resend,
  unsent,
}: {
  request: BrokerRequestDetail
  resend: ReturnType<typeof useBrokerAction>
  unsent: boolean
}) {
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
 *
 * **The five car sides are excluded** (slice 6.1). They arrive from the same query — the list selects
 * on the owner alone, which is what makes a customer's files reach B4 and the email with no extra
 * code — but `CarPhotosPanel` renders them by side, and listing them again here as five rows called
 * `PLACEHOLDER-public_car_front.jpg` is the same evidence twice in a worse form. It also restores the
 * B4Review artboard's two sections: "The car", then "Supporting documents".
 */
function Documents({ requestId, title = 'Documents' }: { requestId: string; title?: string }) {
  const { data, isPending, error } = useRequestDocuments(requestId)

  if (isPending) return <p className="muted">Loading documents…</p>
  if (error) return <AlertBanner>The documents could not be loaded.</AlertBanner>

  const documents = (data ?? []).filter((document) => !CAR_SHOT_BUCKETS.includes(document.bucket))
  if (documents.length === 0) return null

  return (
    <section className="panel">
      <h3 className="panel__title">{title}</h3>
      {documents.map((document) => (
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

/**
 * B4's photo review (design.md §5.3, slice 6.1) — the customer's five car sides, each in its own
 * labelled slot.
 *
 * **A side with no document says "Not provided" rather than being omitted.** Two reasons: a request
 * that reached `ready_to_send` before this slice existed legitimately has none — 5.3 shipped the form
 * without the capture — and a review screen that silently showed four photographs would read as a
 * customer who sent four, which is exactly the misreading the placeholder this replaces was written
 * to prevent.
 */
function CarPhotosPanel({ requestId }: { requestId: string }) {
  const { data, isPending, error } = useRequestDocuments(requestId)

  if (isPending) return <p className="muted">Loading photographs…</p>
  if (error) return <AlertBanner>The photographs could not be loaded.</AlertBanner>

  return (
    <section className="panel">
      <h3 className="panel__title">The car</h3>
      <p className="caption">Five sides, every one mandatory before the customer could send.</p>
      <div className="car-slots">
        {CAR_SHOT_PANELS.map((panel) => (
          <CarSlot
            key={panel.bucket}
            requestId={requestId}
            label={panel.label}
            document={(data ?? []).find((document) => document.bucket === panel.bucket)}
          />
        ))}
      </div>
    </section>
  )
}

function CarSlot({
  requestId,
  label,
  document,
}: {
  requestId: string
  label: string
  document: BrokerDocument | undefined
}) {
  // A swept blob is a real and expected state (§7.3 deletes these `Retention:BrokerBlobDays` after
  // the email went), so the row says so rather than fetching bytes that are gone and showing a broken
  // image — `blobRetained` is the flag the list carries for exactly this.
  const preview = useDocumentBlobUrl(
    document ? requestDocumentContentPath(requestId, document.id) : '',
    { enabled: document !== undefined && document.blobRetained },
  )

  return (
    <figure className="car-slot">
      <span className="car-slot__frame">
        {preview.url ? (
          <img src={preview.url} alt={`${label} of the car`} />
        ) : (
          <span className="muted">{describeSlot(document, preview.pending, preview.failed)}</span>
        )}
      </span>
      <figcaption className="car-slot__label">{label}</figcaption>
    </figure>
  )
}

function describeSlot(
  document: BrokerDocument | undefined,
  pending: boolean,
  failed: boolean,
): string {
  if (!document) return 'Not provided'
  if (!document.blobRetained) return 'Local copy removed'
  if (failed) return 'Could not be shown'
  return pending ? 'Loading…' : 'Not provided'
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
