import { useQuery } from '@tanstack/react-query'
import { expertKeys, getAssignment, listAssignments } from './api'

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
