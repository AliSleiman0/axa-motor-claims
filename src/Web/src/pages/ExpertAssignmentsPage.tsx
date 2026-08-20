import { Link } from 'react-router-dom'
import { ApiError } from '../api/client'
import { formatDateTime } from '../api/datetime'
import { useAssignments } from '../expert/useAssignments'

/** E1 (design.md §5.1): "lists assignments newest-first with media counts; that is the whole lifecycle UI". */
export default function ExpertAssignmentsPage() {
  const { data, isPending, error } = useAssignments()

  if (isPending) return <p>Loading claims…</p>
  if (error) {
    return (
      <p role="alert">
        Could not load claims{error instanceof ApiError ? ` (${error.status})` : ''}.
      </p>
    )
  }

  if (data.length === 0) return <p>No claims assigned yet.</p>

  return (
    <section>
      <h2>My claims</h2>
      <table border={1} cellPadding={4}>
        <thead>
          <tr>
            <th>Visa</th>
            <th>Plate</th>
            <th>Insured</th>
            <th>Vehicle</th>
            <th>Accident</th>
            <th>Received</th>
            <th>Media</th>
            <th>Arrived</th>
          </tr>
        </thead>
        <tbody>
          {data.map((item) => (
            <tr key={item.id}>
              <td>
                <Link to={`/expert/${item.id}`}>{item.visaNo}</Link>
              </td>
              {/* Null claim fields are a cold cache, not missing data — say so rather than blank. */}
              <td>{item.plateNo ?? '—'}</td>
              <td>{item.insuredName ?? '—'}</td>
              <td>{item.carMakeModel ?? '—'}</td>
              <td>{item.accidentDate ?? '—'}</td>
              <td>{formatDateTime(item.receivedAt)}</td>
              <td>{item.mediaCount}</td>
              <td>{item.arrivedAt ? formatDateTime(item.arrivedAt) : '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}
