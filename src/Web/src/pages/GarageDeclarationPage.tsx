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

/**
 * G3 (design.md §5.2): one declaration, rendered according to its state.
 *
 * **The branching below is §5.2's transition table seen from the other side.** What a garage may do
 * next is a property of the state, so the screen shows the controls for that state and no others —
 * a Submit button on an approved declaration would be a 409 waiting to be pressed.
 */
export default function GarageDeclarationPage() {
  const { id } = useParams()
  if (!id) return <p role="alert">No declaration selected.</p>
  return <DeclarationView declarationId={id} />
}

function DeclarationView({ declarationId }: { declarationId: string }) {
  const { data, isPending, error } = useDeclaration(declarationId)

  if (isPending) return <p>Loading declaration…</p>
  if (error) return <p role="alert">{describe(error)}</p>

  return (
    <section>
      <p>
        <Link to="/garage">← My declarations</Link>
      </p>
      <h2>Declaration {data.plateNo}</h2>
      <p>
        Status: <strong>{STATE_LABELS[data.state]}</strong>
      </p>

      <DeclarationFields detail={data} />

      {data.state === 'draft' && (
        <DraftPanel declarationId={declarationId} detail={data} />
      )}
      {data.state === 'submitted' && <WaitingPanel />}
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
    <table border={1} cellPadding={4}>
      <tbody>
        <Row label="Plate" value={detail.plateNo} />
        <Row label="Insured" value={detail.insuredName} />
        <Row label="Note" value={detail.note} />
        <Row label="Created" value={formatDateTime(detail.createdAt)} />
        <Row label="Submitted" value={detail.submittedAt ? formatDateTime(detail.submittedAt) : null} />
        <Row label="Decided" value={detail.decidedAt ? formatDateTime(detail.decidedAt) : null} />
      </tbody>
    </table>
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
      <section>
        <h3>Photos and documents</h3>
        <p role="alert">
          Photo quality settings could not be loaded, so nothing can be sent yet. Reload the page.
        </p>
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
    <section>
      <h3>Photos and documents</h3>

      <CapturePanel
        path={path}
        bucket="garage_documents"
        label="Survey documents"
        config={config}
        count={countFor('garage_documents')}
        onUploaded={refresh}
      />
      <CapturePanel
        path={path}
        bucket="garage_car_photo"
        label="Car photos"
        config={config}
        count={countFor('garage_car_photo')}
        onUploaded={refresh}
      />

      <h3>Send to AXA</h3>
      <p>
        {canSubmit
          ? 'AXA will review this declaration and link it to a claim.'
          : 'Add at least one photo or document before sending this to AXA.'}
      </p>
      <button type="button" disabled={!canSubmit || submit.pending} onClick={submit.run}>
        {submit.pending ? 'Sending…' : 'Submit to AXA'}
      </button>
      {submit.failed && <p role="alert">{submit.failed}</p>}
      {detail.note && <p>Note: {detail.note}</p>}
    </section>
  )
}

function WaitingPanel() {
  return (
    <section>
      <h3>Waiting for AXA</h3>
      <p>
        This declaration is with a claim officer. You will get a notification on this device when it
        has been reviewed.
      </p>
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
    <section>
      <h3>Not accepted</h3>
      <p>AXA did not accept this declaration. File a new one if the repair should still go ahead.</p>
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
    <section>
      <h3>Claim {detail.visaNo}</h3>

      {detail.claimStatus === 'stale' && (
        <p role="alert">
          NEXT3 is unreachable. Showing the copy last fetched
          {detail.claimFetchedAt ? ` on ${formatDateTime(detail.claimFetchedAt)}` : ''}.
        </p>
      )}

      <table border={1} cellPadding={4}>
        <tbody>
          <Row label="Visa" value={detail.visaNo} />
          <Row label="Policy" value={detail.claim?.policyNo} />
          <Row label="Plate" value={detail.claim?.plateNo} />
          <Row label="Insured" value={detail.claim?.insuredName} />
          <Row label="Insured phone" value={detail.claim?.insuredPhone} />
          <Row label="Vehicle" value={detail.claim?.carMakeModel} />
          <Row label="City" value={detail.claim?.city} />
          <Row label="Accident date" value={detail.claim?.accidentDate} />
        </tbody>
      </table>

      <h3>AXA comments</h3>
      {detail.comments.length === 0 ? (
        <p>No comments.</p>
      ) : (
        <ul>
          {detail.comments.map((comment) => (
            <li key={`${comment.createdAt}-${comment.body}`}>
              {comment.body} <em>({formatDateTime(comment.createdAt)})</em>
            </li>
          ))}
        </ul>
      )}

      {detail.state === 'approved' && (
        <section>
          <h3>Repairs</h3>
          <button type="button" disabled={startRepairs.pending} onClick={startRepairs.run}>
            {startRepairs.pending ? 'Starting…' : 'Start repairs'}
          </button>
          {startRepairs.failed && <p role="alert">{startRepairs.failed}</p>}
        </section>
      )}

      {detail.state === 'repairs_in_progress' && (
        <section>
          <h3>Repairs in progress</h3>
          {/* Named rather than hidden: a garage that has started repairs will look for where to send
              the invoice, and "not built yet" is a better answer than a screen with nothing on it. */}
          <p>
            Sending the discharge, the invoice and the post-repair photos is not part of this release
            yet (slice 5.1). Send them to AXA the way you do today.
          </p>
        </section>
      )}

      {detail.state === 'repair_docs_submitted' && (
        <section>
          <h3>Repair documents sent</h3>
          <p>This declaration is complete.</p>
        </section>
      )}
    </section>
  )
}

function Row({ label, value }: { label: string; value?: string | null }) {
  return (
    <tr>
      <th align="left">{label}</th>
      <td>{value ?? '—'}</td>
    </tr>
  )
}

function describe(error: Error): string {
  if (error instanceof ApiError && error.status === 404) {
    return 'That declaration is not one of yours.'
  }

  return `Could not load the declaration${error instanceof ApiError ? ` (${error.status})` : ''}.`
}
