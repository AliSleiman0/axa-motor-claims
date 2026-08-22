import { ApiError } from '../api/client'
import { formatDateTime } from '../api/datetime'
import type { AssignmentListItem } from '../expert/api'
import { SEARCH_MAX_LENGTH, useAssignmentSearch } from '../expert/useAssignmentSearch'
import { useAssignments } from '../expert/useAssignments'
import { AlertBanner, StatusBanner } from '../ui/Banner'
import { SearchField } from '../ui/fields'
import { WorklistCard } from '../ui/Worklist'

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
    <section className="page">
      <h2 className="page__title">My claims</h2>

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
    <SearchField
      id="claim-search"
      label="Search visa or plate"
      value={term}
      // The server refuses a longer term outright; this only stops the screen producing one.
      maxLength={SEARCH_MAX_LENGTH}
      className="field__control--mono"
      onChange={(event) => {
        setTerm(event.target.value)
      }}
    />
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
    <StatusBanner>
      Searching visa and plate numbers. A claim whose details have not loaded from NEXT3 yet has no
      plate to match — search that one by visa number.
    </StatusBanner>
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
  if (isPending) return <p className="muted">Loading claims…</p>
  if (error) {
    return (
      <AlertBanner>
        Could not load claims{error instanceof ApiError ? ` (${error.status})` : ''}.
      </AlertBanner>
    )
  }

  if (!data || data.length === 0) {
    // Two different facts that used to share one sentence. "No claims assigned yet" is simply
    // untrue when the expert has claims and has just mistyped a plate.
    return query ? (
      <div className="stack">
        <p className="muted">No claim of yours matches “{query}”.</p>
        {/*
          §5.1's search is local to this expert's own assignments — a NEXT3-wide search would return
          claims they were never assigned, onto a screen carrying a capture panel. An expert who does
          not know that reads an empty result as "AXA has lost the claim" rather than "not mine".
        */}
        <p className="muted">
          Search only reaches the claims assigned to you. Clear the box to see all of them.
        </p>
      </div>
    ) : (
      <p className="muted">No claims assigned yet.</p>
    )
  }

  return (
    <div className="worklist-cards">
      {data.map((item) => (
        <WorklistCard
          key={item.id}
          /*
            The search rides along into E2, which hands it back on its "My claims" link. Found in
            the browser: without it an expert who searches, opens a claim and comes back has to
            retype the plate — on a phone, at a crash site, which is the situation the whole feature
            exists for. The URL was already the source of truth; this just keeps it.
          */
          to={`/expert/${item.id}${search}`}
          reference={item.visaNo}
          /* The count is its own element: "3 media" as one string would make the number
             unaddressable, and it is the column §5.1 says E1 exists to show. */
          status={
            <span className="caption">
              Media <strong>{item.mediaCount}</strong>
            </span>
          }
          meta={[
            // Null claim fields are a cold cache, not missing data — say so rather than blank.
            { label: 'Plate', value: item.plateNo ?? '—' },
            { label: 'Insured', value: item.insuredName ?? '—' },
            { label: 'Vehicle', value: item.carMakeModel ?? '—' },
            { label: 'Accident', value: item.accidentDate ?? '—' },
            { label: 'Received', value: formatDateTime(item.receivedAt) },
            { label: 'Arrived', value: item.arrivedAt ? formatDateTime(item.arrivedAt) : '—' },
          ]}
        />
      ))}
    </div>
  )
}
