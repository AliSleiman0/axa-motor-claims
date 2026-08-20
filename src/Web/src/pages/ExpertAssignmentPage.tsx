import { Link, useParams } from 'react-router-dom'
import { ApiError } from '../api/client'
import { formatDateTime } from '../api/datetime'
import { documentsPath, type AssignmentDetail } from '../expert/api'
import { GEOLOCATION_EXPLANATIONS } from '../expert/geolocation'
import { useArrived } from '../expert/useArrived'
import {
  useAssignment,
  useAssignmentDocuments,
  useRefreshAfterCapture,
} from '../expert/useAssignments'
import { CapturePanel } from '../media/CapturePanel'
import { useMediaConfig } from '../media/useMediaConfig'

/** E2 (design.md §5.1): claim detail plus the Arrived button. */
export default function ExpertAssignmentPage() {
  const { id } = useParams()
  if (!id) return <p role="alert">No claim selected.</p>
  return <AssignmentDetailView assignmentId={id} />
}

function AssignmentDetailView({ assignmentId }: { assignmentId: string }) {
  const { data, isPending, error } = useAssignment(assignmentId)

  if (isPending) return <p>Loading claim…</p>
  if (error) return <p role="alert">{describe(error)}</p>

  return (
    <section>
      <p>
        <Link to="/expert">← My claims</Link>
      </p>
      <h2>Claim {data.visaNo}</h2>

      {data.claimStatus === 'stale' && (
        // §4: "if NEXT3 is down, serve stale with a staleness banner". The age is the age of the
        // data, not of the request — showing it is the difference between honest and reassuring.
        <p role="alert">
          NEXT3 is unreachable. Showing the copy last fetched
          {data.claimFetchedAt ? ` on ${formatDateTime(data.claimFetchedAt)}` : ''}.
        </p>
      )}
      {data.claimStatus === 'not_found' && (
        <p role="alert">NEXT3 does not have a claim under this visa number.</p>
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

/** §7.1's four expert buckets. `expert_report` is E5 and belongs to slice 3.2. */
const EXPERT_BUCKETS = [
  { bucket: 'insured_documents', label: 'Insured documents' },
  { bucket: 'insured_car_photo', label: 'Insured car photos' },
  { bucket: 'tp_documents', label: 'Third-party documents' },
  { bucket: 'tp_car_photo', label: 'Third-party car photos' },
]

/** E3 (design.md §5.1): the four buckets, each through §7.2's clarity gate. */
function CaptureSection({ assignmentId }: { assignmentId: string }) {
  const { data: config, error } = useMediaConfig()
  const { data: documents } = useAssignmentDocuments(assignmentId)
  const refresh = useRefreshAfterCapture(assignmentId)
  const path = documentsPath(assignmentId)

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

  return (
    <section>
      <h3>Photos and documents</h3>
      {EXPERT_BUCKETS.map((entry) => (
        <CapturePanel
          key={entry.bucket}
          path={path}
          bucket={entry.bucket}
          label={entry.label}
          config={config}
          count={documents?.filter((document) => document.bucket === entry.bucket).length}
          onUploaded={refresh}
        />
      ))}
    </section>
  )
}

function ClaimFields({ detail }: { detail: AssignmentDetail }) {
  const claim = detail.claim
  return (
    <table border={1} cellPadding={4}>
      <tbody>
        <Row label="Visa" value={detail.visaNo} />
        <Row label="Policy" value={claim?.policyNo} />
        <Row label="Plate" value={claim?.plateNo} />
        <Row label="Insured" value={claim?.insuredName} />
        <Row label="Insured phone" value={claim?.insuredPhone} />
        <Row label="Vehicle" value={claim?.carMakeModel} />
        <Row label="City" value={claim?.city} />
        <Row label="Accident date" value={claim?.accidentDate} />
        <Row label="Assigned" value={formatDateTime(detail.receivedAt)} />
      </tbody>
    </table>
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

function ArrivedPanel({
  assignmentId,
  arrivedAt,
}: {
  assignmentId: string
  arrivedAt: string | null
}) {
  const arrival = useArrived(assignmentId, arrivedAt)

  return (
    <section>
      <h3>Arrival</h3>
      <button
        type="button"
        // §5.1: "Button disabled after first press."
        disabled={arrival.arrived || arrival.pending}
        onClick={arrival.press}
      >
        {arrival.pending ? 'Sending…' : 'Arrived'}
      </button>{' '}
      {arrival.arrived && arrival.arrivedAt && (
        <span>Arrived at {formatDateTime(arrival.arrivedAt)}</span>
      )}
      {arrival.blockedBy && <p role="alert">{GEOLOCATION_EXPLANATIONS[arrival.blockedBy]}</p>}
      {arrival.failed && <p role="alert">{arrival.failed}</p>}
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
