import { Link } from 'react-router-dom'
import { ApiError } from '../api/client'
import { formatDateTime } from '../api/datetime'
import type { AssignmentListItem } from '../expert/api'
import { SEARCH_MAX_LENGTH, useAssignmentSearch } from '../expert/useAssignmentSearch'
import { useAssignments } from '../expert/useAssignments'

/**
 * E1 (design.md §5.1): "lists assignments newest-first with media counts; that is the whole
 * lifecycle UI", plus slice 3.2's search.
 *
 * @param debounceMs
 * Injected the way `decode`, `render` and `start` are injected across `media/`: fake timers
 * deadlock against @testing-library's async helpers here, and a debounce cannot be proven with a
 * delay of zero — the test needs a real but short one. Omitted in production, and omitting it
 * re-selects the default rather than passing `undefined` through (2.5's trap, deliberately fine
 * here because the default *is* what production wants).
 */
export default function ExpertAssignmentsPage({ debounceMs }: { debounceMs?: number } = {}) {
  const { term, setTerm, query } = useAssignmentSearch(debounceMs)
  const search = query ? `?${new URLSearchParams({ q: query }).toString()}` : ''
  const { data, isPending, error } = useAssignments(query)

  return (
    <section>
      <h2>My claims</h2>

      {/*
        The search box is rendered above every branch below on purpose. Put it inside them and the
        first `isPending` return unmounts the input mid-typing, taking the caret and the focus with
        it — invisible in a test, immediately fatal on a phone.
      */}
      <SearchBox term={term} setTerm={setTerm} />
      {query && <ColdCacheHint />}

      <Results data={data} isPending={isPending} error={error} query={query} search={search} />
    </section>
  )
}

function SearchBox({ term, setTerm }: { term: string; setTerm: (next: string) => void }) {
  return (
    <p>
      <label htmlFor="claim-search">Search visa or plate</label>{' '}
      <input
        id="claim-search"
        type="search"
        value={term}
        // The server refuses a longer term outright; this only stops the screen producing one.
        maxLength={SEARCH_MAX_LENGTH}
        onChange={(event) => {
          setTerm(event.target.value)
        }}
      />
    </p>
  )
}

/**
 * §5.1's search matches the visa number on the assignment and the plate on the *cached* claim. An
 * assignment that arrived while NEXT3 was down has no cached claim at all, so it has no plate to
 * match — findable by visa only, until NEXT3 answers. Saying so is cheaper than an expert deciding
 * the search is broken.
 */
function ColdCacheHint() {
  return (
    <p>
      Searching visa and plate numbers. A claim whose details have not loaded from NEXT3 yet has no
      plate to match — search that one by visa number.
    </p>
  )
}

function Results({
  data,
  isPending,
  error,
  query,
  search,
}: {
  data: AssignmentListItem[] | undefined
  isPending: boolean
  error: Error | null
  query: string
  /** The live query string, carried into each claim link so E2 can return the expert to it. */
  search: string
}) {
  if (isPending) return <p>Loading claims…</p>
  if (error) {
    return (
      <p role="alert">
        Could not load claims{error instanceof ApiError ? ` (${error.status})` : ''}.
      </p>
    )
  }

  if (!data || data.length === 0) {
    // Two different facts that used to share one sentence. "No claims assigned yet" is simply
    // untrue when the expert has claims and has just mistyped a plate.
    return query ? (
      <p>No claim of yours matches “{query}”.</p>
    ) : (
      <p>No claims assigned yet.</p>
    )
  }

  return (
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
              {/*
                The search rides along into E2, which hands it back on its "My claims" link. Found
                in the browser: without it an expert who searches, opens a claim and comes back has
                to retype the plate — on a phone, at a crash site, which is the situation the whole
                feature exists for. The URL was already the source of truth; this just keeps it.
              */}
              <Link to={`/expert/${item.id}${search}`}>{item.visaNo}</Link>
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
  )
}
