import { Link, useParams } from 'react-router-dom'
import { ApiError } from '../api/client'
import { formatDateTime } from '../api/datetime'
import { declarationDocumentsPath, type DeclarationDetail } from '../garage/api'
import { STATE_LABELS } from '../garage/labels'
import {
  useDeclaration,
  useDeclarationAction,
  useDeclarationDocuments,
  useRefreshAfterCapture,
} from '../garage/useDeclarations'
import { CapturePanel } from '../media/CapturePanel'
import { useMediaConfig } from '../media/useMediaConfig'
import { AlertBanner, StatusBanner } from '../ui/Banner'
import { Button } from '../ui/Button'
import { DetailTable } from '../ui/DetailTable'
import { DocumentRow, PushIndicator } from '../ui/DocumentRow'
import { StatusChip } from '../ui/StatusChip'

/**
 * G3 (design.md §5.2): one declaration, rendered according to its state.
 *
 * **The branching below is §5.2's transition table seen from the other side.** What a garage may do
 * next is a property of the state, so the screen shows the controls for that state and no others —
 * a Submit button on an approved declaration would be a 409 waiting to be pressed.
 */
export default function GarageDeclarationPage() {
  const { id } = useParams()
  if (!id) return <AlertBanner>No declaration selected.</AlertBanner>
  return <DeclarationView declarationId={id} />
}

function DeclarationView({ declarationId }: { declarationId: string }) {
  const { data, isPending, error } = useDeclaration(declarationId)

  if (isPending) return <p className="muted">Loading declaration…</p>
  if (error) return <AlertBanner>{describe(error)}</AlertBanner>

  return (
    <section className="page">
      <Link className="back-link" to="/garage">
        ← My declarations
      </Link>
      <div className="page__head">
        <h2 className="page__title">Declaration {data.plateNo}</h2>
        {/* The chip and the sentence both: the demo script and four tests read "Status: <label>",
            and the chip is what makes the state scannable down a phone screen. */}
        <StatusChip label={STATE_LABELS[data.state]} />
      </div>
      <p className="muted">
        Status: <strong>{STATE_LABELS[data.state]}</strong>
      </p>

      <DeclarationFields detail={data} />

      {data.state === 'draft' && (
        <DraftPanel declarationId={declarationId} detail={data} />
      )}
      {data.state === 'submitted' && <WaitingPanel declarationId={declarationId} />}
      {data.state === 'rejected' && <RejectedPanel />}
      {(data.state === 'approved' ||
        data.state === 'repairs_in_progress' ||
        data.state === 'repair_docs_submitted') && (
        <ApprovedPanel declarationId={declarationId} detail={data} />
      )}
    </section>
  )
}

function DeclarationFields({ detail }: { detail: DeclarationDetail }) {
  return (
    <DetailTable
      rows={[
        { label: 'Plate', value: detail.plateNo, mono: true },
        { label: 'Insured', value: detail.insuredName },
        { label: 'Note', value: detail.note },
        { label: 'Created', value: formatDateTime(detail.createdAt) },
        { label: 'Submitted', value: detail.submittedAt ? formatDateTime(detail.submittedAt) : null },
        { label: 'Decided', value: detail.decidedAt ? formatDateTime(detail.decidedAt) : null },
      ]}
    />
  )
}

/**
 * `draft` — the only state a garage may attach documents in, and the only one with Submit.
 *
 * §7.1's two garage buckets: documents allow either provenance, car photos are **capture-only**,
 * which the panel expresses by rendering no file picker at all. Both post to the declaration's own
 * documents path — `useCapture` has taken that as a parameter since 2.5 precisely so this screen
 * could reuse it unchanged.
 */
function DraftPanel({
  declarationId,
  detail,
}: {
  declarationId: string
  detail: DeclarationDetail
}) {
  const { data: config, error } = useMediaConfig()
  const { data: documents } = useDeclarationDocuments(declarationId)
  const refresh = useRefreshAfterCapture(declarationId)
  const submit = useDeclarationAction(declarationId, 'submit')
  const path = declarationDocumentsPath(declarationId)

  if (error) {
    return (
      <section className="stack">
        <h3 className="section-title">Photos and documents</h3>
        <AlertBanner>
          Photo quality settings could not be loaded, so nothing can be sent yet. Reload the page.
        </AlertBanner>
      </section>
    )
  }

  const countFor = (bucket: string) =>
    documents?.filter((document) => document.bucket === bucket).length

  // §5.2 does not say a declaration needs documents to be submitted, and the server does not enforce
  // it. This is the smaller interpretation applied to the *screen*: a declaration with no photographs
  // is one an officer can only reject, and letting it be sent wastes a round trip through a person.
  // Recorded in scope-decisions.md; the server stays permissive, so nothing here is a control.
  const documentCount = documents?.length ?? 0
  const canSubmit = documentCount > 0

  return (
    <section className="stack">
      <h3 className="section-title">Photos and documents</h3>

      <CapturePanel
        path={path}
        bucket="garage_documents"
        label="Survey documents"
        config={config}
        count={countFor('garage_documents')}
        onUploaded={refresh}
        qualifier="survey document"
      />
      <CapturePanel
        path={path}
        bucket="garage_car_photo"
        label="Car photos"
        config={config}
        count={countFor('garage_car_photo')}
        onUploaded={refresh}
        qualifier="car photo"
      />

      <div className="panel">
        <h3 className="panel__title">Send to AXA</h3>
        <p className="muted">
          {canSubmit
            ? 'AXA will review this declaration and link it to a claim.'
            : 'Add at least one photo or document before sending this to AXA.'}
        </p>
        <div className="actions">
          <Button
            variant="primary"
            disabled={!canSubmit || submit.pending}
            onClick={submit.run}
          >
            {submit.pending ? 'Sending…' : 'Submit to AXA'}
          </Button>
        </div>
        {submit.failed && <AlertBanner>{submit.failed}</AlertBanner>}
        {detail.note && <p className="muted">Note: {detail.note}</p>}
      </div>
    </section>
  )
}

function WaitingPanel({ declarationId }: { declarationId: string }) {
  return (
    <section className="stack">
      <div className="panel">
        <h3 className="panel__title">Waiting for AXA</h3>
        <p className="muted">
          This declaration is with a claim officer. You will get a notification on this device when
          it has been reviewed.
        </p>
        {/* No action, deliberately: there is no way to edit or withdraw a submitted declaration,
            because an officer is already looking at it. */}
      </div>
      <SubmittedDocuments declarationId={declarationId} />
    </section>
  )
}

/**
 * "What was sent" — the garage's own evidence, while the declaration is with an officer.
 *
 * It answers the question the waiting state otherwise leaves open: *did my photographs go
 * anywhere?* And the honest answer is **"Queued, will send"** rather than "sent", because §5.2 holds
 * every garage document at `push_status = deferred` with no outbox row at all until an officer
 * approves and picks the visa it belongs under. `PushIndicator` renders nothing for `deferred`,
 * which is right — before a decision there is no push to be in a state about.
 *
 * Names and buckets only: no preview and no blob fetch. The artboard draws a file name and a status,
 * and previewing here would mean lifting `useDocumentBlobUrl` out of `officer/` into shared code for
 * a screen whose job is reassurance rather than review.
 */
function SubmittedDocuments({ declarationId }: { declarationId: string }) {
  const { data, isPending, error } = useDeclarationDocuments(declarationId)

  if (isPending) return <p className="muted">Loading documents…</p>
  if (error) return <AlertBanner>The documents could not be loaded.</AlertBanner>
  if (!data || data.length === 0) return null

  return (
    <section className="panel">
      <h3 className="panel__title">What was sent</h3>
      {data.map((document) => (
        <DocumentRow
          key={document.id}
          name={document.fileName ?? document.bucket}
          bucket={document.bucket}
          indicator={
            <PushIndicator
              pushStatus={document.pushStatus}
              blobRetained={document.blobRetained}
            />
          }
        />
      ))}
    </section>
  )
}

/**
 * `rejected` — **status only, and no comments** (§1).
 *
 * The BRD grants comment visibility "in case of confirmation" only, so the officer's reason is stored
 * and never shown here. The server does not send it either, so this is not a screen hiding something
 * it holds. It will surprise users, and the scope letter has to say so.
 */
function RejectedPanel() {
  return (
    <section className="panel">
      <h3 className="panel__title">Not accepted</h3>
      <p className="muted">
        AXA did not accept this declaration. File a new one if the repair should still go ahead.
      </p>
      {/* Terminal, and the screen says what to do next rather than leaving it to be discovered. */}
      <div className="actions">
        <Link className="btn btn--primary" to="/garage/new">
          New declaration
        </Link>
      </div>
    </section>
  )
}

/** `approved` and beyond — §5.2's "approval unlocks the full claim detail". */
function ApprovedPanel({
  declarationId,
  detail,
}: {
  declarationId: string
  detail: DeclarationDetail
}) {
  const startRepairs = useDeclarationAction(declarationId, 'start-repairs')

  return (
    <section className="stack">
      <h3 className="section-title">Claim {detail.visaNo}</h3>

      {detail.claimStatus === 'stale' && (
        // Amber, not red (pass 3): an outage passes on its own, and the claim below is still true.
        <AlertBanner intent="warn">
          NEXT3 is unreachable. Showing the copy last fetched
          {detail.claimFetchedAt ? ` on ${formatDateTime(detail.claimFetchedAt)}` : ''}.
        </AlertBanner>
      )}

      <DetailTable
        rows={[
          { label: 'Visa', value: detail.visaNo, mono: true },
          { label: 'Policy', value: detail.claim?.policyNo, mono: true },
          { label: 'Plate', value: detail.claim?.plateNo, mono: true },
          { label: 'Insured', value: detail.claim?.insuredName },
          { label: 'Insured phone', value: detail.claim?.insuredPhone, mono: true, tel: true },
          { label: 'Vehicle', value: detail.claim?.carMakeModel },
          { label: 'City', value: detail.claim?.city },
          { label: 'Accident date', value: detail.claim?.accidentDate },
        ]}
      />

      <div className="panel">
        <h3 className="panel__title">AXA comments</h3>
        {detail.comments.length === 0 ? (
          <p className="muted">No comments.</p>
        ) : (
          <ul className="stack">
            {detail.comments.map((comment) => (
              <li key={`${comment.createdAt}-${comment.body}`}>
                {comment.body} <em className="caption">({formatDateTime(comment.createdAt)})</em>
              </li>
            ))}
          </ul>
        )}
      </div>

      {detail.state === 'approved' && (
        <section className="panel">
          <h3 className="panel__title">Repairs</h3>
          <div className="actions">
            <Button variant="primary" disabled={startRepairs.pending} onClick={startRepairs.run}>
              {startRepairs.pending ? 'Starting…' : 'Start repairs'}
            </Button>
          </div>
          {startRepairs.failed && <AlertBanner>{startRepairs.failed}</AlertBanner>}
        </section>
      )}

      {detail.state === 'repairs_in_progress' && (
        <section className="panel">
          <h3 className="panel__title">Repairs in progress</h3>
          {/* Named rather than hidden: a garage that has started repairs will look for where to send
              the invoice, and "not built yet" is a better answer than a screen with nothing on it. */}
          <StatusBanner>
            Sending the discharge, the invoice and the post-repair photos is not part of this release
            yet (slice 5.1). Send them to AXA the way you do today.
          </StatusBanner>
        </section>
      )}

      {detail.state === 'repair_docs_submitted' && (
        <section className="panel">
          <h3 className="panel__title">Repair documents sent</h3>
          <p className="muted">This declaration is complete.</p>
        </section>
      )}
    </section>
  )
}

function describe(error: Error): string {
  if (error instanceof ApiError && error.status === 404) {
    return 'That declaration is not one of yours.'
  }

  return `Could not load the declaration${error instanceof ApiError ? ` (${error.status})` : ''}.`
}
