import { Link, useLocation, useParams } from 'react-router-dom'
import { ApiError } from '../api/client'
import { formatDateTime } from '../api/datetime'
import { documentsPath, type AssignmentDetail } from '../expert/api'
import { explainGeolocationFailure } from '../expert/geolocation'
import { detectNativeShell } from '../media/nativeShell'
import { useArrived } from '../expert/useArrived'
import {
  useAssignment,
  useAssignmentDocuments,
  useRefreshAfterCapture,
} from '../expert/useAssignments'
import { CapturePanel } from '../media/CapturePanel'
import { VoicePanel } from '../media/VoicePanel'
import { DiagramPanel } from '../media/diagram/DiagramPanel'
import { useMediaConfig } from '../media/useMediaConfig'
import { AlertBanner } from '../ui/Banner'
import { Button } from '../ui/Button'
import { DetailTable } from '../ui/DetailTable'

/** E2 (design.md §5.1): claim detail plus the Arrived button. */
export default function ExpertAssignmentPage() {
  const { id } = useParams()
  if (!id) return <AlertBanner>No claim selected.</AlertBanner>
  return <AssignmentDetailView assignmentId={id} />
}

function AssignmentDetailView({ assignmentId }: { assignmentId: string }) {
  const { data, isPending, error } = useAssignment(assignmentId)
  // E1 puts its active search in the link that got us here, so going back returns to the filtered
  // list rather than to the expert's whole history (slice 3.2, found in the browser pass).
  const { search } = useLocation()

  if (isPending) return <p className="muted">Loading claim…</p>
  if (error) return <AlertBanner>{describe(error)}</AlertBanner>

  return (
    <section className="page">
      <Link className="back-link" to={`/expert${search}`}>
        ← My claims
      </Link>
      <h2 className="page__title">Claim {data.visaNo}</h2>

      {data.claimStatus === 'stale' && (
        // §4: "if NEXT3 is down, serve stale with a staleness banner". The age is the age of the
        // data, not of the request — showing it is the difference between honest and reassuring.
        // Amber rather than red (pass 3): an outage passes on its own and names no action.
        <AlertBanner intent="warn">
          NEXT3 is unreachable. Showing the copy last fetched
          {data.claimFetchedAt ? ` on ${formatDateTime(data.claimFetchedAt)}` : ''}.
        </AlertBanner>
      )}
      {data.claimStatus === 'not_found' && (
        <AlertBanner>NEXT3 does not have a claim under this visa number.</AlertBanner>
      )}

      <ClaimFields detail={data} />
      <ArrivedPanel assignmentId={assignmentId} arrivedAt={data.arrivedAt} />

      {/*
        Nothing below the Arrived panel may be gated on arrival. §5.1's recorded interpretation:
        the diagram implies an order, the BRD never states the gate, and an expert whose GPS is slow
        must not be blocked from photographing the car. Note the capture section below takes no
        arrival prop at all — the guarantee is structural, not a condition someone can flip.
      */}
      <CaptureSection assignmentId={assignmentId} />
    </section>
  )
}

/**
 * §7.1's five expert buckets. `expert_report` is E5 (slice 3.2) and needed nothing but this line:
 * the bucket, its doc type and PDF acceptance all shipped server-side in 2.3, and §5.1 gives the
 * report no screen of its own — the smaller interpretation is a panel here, not a route.
 */
const EXPERT_BUCKETS = [
  { bucket: 'insured_documents', label: 'Insured documents', qualifier: 'insured document' },
  { bucket: 'insured_car_photo', label: 'Insured car photos', qualifier: 'insured car' },
  { bucket: 'tp_documents', label: 'Third-party documents', qualifier: 'third-party document' },
  { bucket: 'tp_car_photo', label: 'Third-party car photos', qualifier: 'third-party car' },
  { bucket: 'expert_report', label: 'Expert report', qualifier: 'expert report' },
]

/** §5.1's other two expert artifacts (slice 3.1) — neither is a file the expert picks. */
const VOICE_BUCKET = 'voice_note'
const DIAGRAM_BUCKET = 'damage_diagram'

/** E3 and E5 (design.md §5.1): the five buckets, each through §7.2's clarity gate. */
function CaptureSection({ assignmentId }: { assignmentId: string }) {
  const { data: config, error } = useMediaConfig()
  const { data: documents } = useAssignmentDocuments(assignmentId)
  const refresh = useRefreshAfterCapture(assignmentId)
  const path = documentsPath(assignmentId)

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

  return (
    <section className="stack">
      <h3 className="section-title">Photos and documents</h3>
      {EXPERT_BUCKETS.map((entry) => (
        <CapturePanel
          key={entry.bucket}
          path={path}
          bucket={entry.bucket}
          label={entry.label}
          config={config}
          count={countFor(entry.bucket)}
          onUploaded={refresh}
          qualifier={entry.qualifier}
        />
      ))}

      <VoicePanel
        path={path}
        bucket={VOICE_BUCKET}
        label="Voice note"
        config={config}
        count={countFor(VOICE_BUCKET)}
        onUploaded={refresh}
      />

      <DiagramPanel
        path={path}
        bucket={DIAGRAM_BUCKET}
        label="Damage diagram"
        config={config}
        count={countFor(DIAGRAM_BUCKET)}
        onUploaded={refresh}
      />
    </section>
  )
}

function ClaimFields({ detail }: { detail: AssignmentDetail }) {
  const claim = detail.claim
  return (
    <DetailTable
      rows={[
        { label: 'Visa', value: detail.visaNo, mono: true },
        { label: 'Policy', value: claim?.policyNo, mono: true },
        { label: 'Plate', value: claim?.plateNo, mono: true },
        { label: 'Insured', value: claim?.insuredName },
        // The one value on this screen that is a link — an expert at the scene rings the insured.
        { label: 'Insured phone', value: claim?.insuredPhone, mono: true, tel: true },
        { label: 'Vehicle', value: claim?.carMakeModel },
        { label: 'City', value: claim?.city },
        { label: 'Accident date', value: claim?.accidentDate },
        { label: 'Assigned', value: formatDateTime(detail.receivedAt) },
      ]}
    />
  )
}

function ArrivedPanel({
  assignmentId,
  arrivedAt,
}: {
  assignmentId: string
  arrivedAt: string | null
}) {
  const arrival = useArrived(assignmentId, arrivedAt)

  return (
    <section className="panel">
      <h3 className="panel__title">Arrival</h3>
      <div className="actions">
        <Button
          variant="primary"
          // §5.1: "Button disabled after first press."
          disabled={arrival.arrived || arrival.pending}
          onClick={arrival.press}
        >
          {arrival.pending ? 'Sending…' : 'Arrived'}
        </Button>
        {arrival.arrived && arrival.arrivedAt && (
          <span className="muted">Arrived at {formatDateTime(arrival.arrivedAt)}</span>
        )}
      </div>
      {/*
        Said before the press, not after. This is the only control on E2 that cannot be undone —
        `WHERE arrived_at IS NULL` makes the guard a property of the database (slice 2.4) — and it
        sends the expert's coordinates, which is worth knowing in advance rather than discovering.
      */}
      {!arrival.arrived && (
        <p className="muted">
          Records the date, the time and where you are, and sends all three to AXA. It can only be
          pressed once.
        </p>
      )}
      {arrival.blockedBy && (
        <AlertBanner>
          {explainGeolocationFailure(arrival.blockedBy, detectNativeShell() !== null)}
        </AlertBanner>
      )}
      {arrival.failed && <AlertBanner>{arrival.failed}</AlertBanner>}
    </section>
  )
}

function describe(error: Error): string {
  if (error instanceof ApiError && error.status === 404) {
    return 'That claim is not assigned to you.'
  }

  if (error instanceof ApiError && error.status === 503) {
    // The 503 the API returns when nothing is cached and NEXT3 is down — an outage, not a missing
    // claim, and worth saying so plainly rather than blaming the data.
    return 'NEXT3 is unreachable and this claim has never been loaded, so there is nothing to show yet.'
  }

  return `Could not load the claim${error instanceof ApiError ? ` (${error.status})` : ''}.`
}
