import { useQuery, useQueryClient } from '@tanstack/react-query'
import { expertKeys, getAssignment, listAssignments, listDocuments } from './api'

/** E1's data. The server orders newest-first (§5.1), so the screen never re-sorts. */
export function useAssignments() {
  return useQuery({
    queryKey: expertKeys.list(),
    queryFn: ({ signal }) => listAssignments(signal),
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
    void queryClient.invalidateQueries({ queryKey: expertKeys.list() })
  }
}
