import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import { expertKeys, getAssignment, listAssignments, listDocuments } from './api'

/**
 * E1's data, optionally filtered by `q` (§5.1's search, local to this expert's own assignments).
 * The server orders newest-first, so the screen never re-sorts.
 *
 * `keepPreviousData` because the term is debounced into this key: without it every committed
 * keystroke flips `isPending` back to true and blanks the table, on the connection that is the
 * scarce resource in this whole application. The old rows stay up until the new ones land.
 */
export function useAssignments(q?: string) {
  return useQuery({
    queryKey: expertKeys.list(q),
    queryFn: ({ signal }) => listAssignments(q, signal),
    placeholderData: keepPreviousData,
  })
}

/** E2's data. The GET also stamps `opened_at` server-side on the first call (§5.1). */
export function useAssignment(assignmentId: string) {
  return useQuery({
    queryKey: expertKeys.detail(assignmentId),
    queryFn: ({ signal }) => getAssignment(assignmentId, signal),
  })
}

/** E3's per-bucket counts of what has already been captured. */
export function useAssignmentDocuments(assignmentId: string) {
  return useQuery({
    queryKey: expertKeys.documents(assignmentId),
    queryFn: ({ signal }) => listDocuments(assignmentId, signal),
  })
}

/**
 * What the expert screens must refresh after a capture lands.
 *
 * The list as well as the documents: E1 shows a media count per assignment (§5.1), so an upload
 * that only invalidated the document list would leave the count stale on the screen the expert goes
 * back to. Same reasoning as `useArrived` invalidating both.
 *
 * It lives here rather than in `media/` on purpose — the capture hook must not know about expert
 * query keys, or it stops being reusable by garage and the public page.
 */
export function useRefreshAfterCapture(assignmentId: string) {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: expertKeys.documents(assignmentId) })
    // `lists()`, not `list()`: the expert may have a search term active, and a key that names the
    // empty term would not match the list actually on screen (see the comment on `expertKeys`).
    void queryClient.invalidateQueries({ queryKey: expertKeys.lists() })
  }
}
