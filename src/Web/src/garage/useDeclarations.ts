import { useRef, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/client'
import {
  type DeclarationState,
  garageKeys,
  getDeclaration,
  listDeclarationDocuments,
  listDeclarations,
  startRepairs,
  submitDeclaration,
  submitRepairDocs,
} from './api'

/**
 * G1's worklist (§5.2). The server orders newest-first, so the screen never re-sorts — the two would
 * silently drift apart, which is E1's rule and the same one here.
 *
 * `keepPreviousData` for E1's reason: a refetch after a transition must not blank the table while the
 * new rows are in flight.
 */
export function useDeclarations() {
  return useQuery({
    queryKey: garageKeys.list(),
    queryFn: ({ signal }) => listDeclarations(signal),
    placeholderData: keepPreviousData,
  })
}

export function useDeclaration(declarationId: string) {
  return useQuery({
    queryKey: garageKeys.detail(declarationId),
    queryFn: ({ signal }) => getDeclaration(declarationId, signal),
  })
}

export function useDeclarationDocuments(declarationId: string) {
  return useQuery({
    queryKey: garageKeys.documents(declarationId),
    queryFn: ({ signal }) => listDeclarationDocuments(declarationId, signal),
  })
}

/**
 * What G3 must refresh after a capture lands.
 *
 * The list as well as the documents: G1 shows a media count per declaration, so an upload that only
 * invalidated the document list would leave the count stale on the screen the garage goes back to.
 * And the **detail**, because Submit is disabled until at least one document exists — without this
 * the button would stay disabled after the first upload until something else happened to refetch.
 *
 * It lives here rather than in `media/` on purpose: the capture hook must not know garage query keys,
 * or it stops being reusable by the Option 2 public page.
 */
export function useRefreshAfterCapture(declarationId: string) {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: garageKeys.documents(declarationId) })
    void queryClient.invalidateQueries({ queryKey: garageKeys.detail(declarationId) })
    // `lists()`, not `list()` — see the comment on `garageKeys`.
    void queryClient.invalidateQueries({ queryKey: garageKeys.lists() })
  }
}

export type DeclarationAction = 'submit' | 'start-repairs' | 'submit-repair-docs'

export interface UseDeclarationActionResult {
  run: () => void
  pending: boolean
  failed: string | null
}

/**
 * One of the garage's three transitions, behind the house latch.
 *
 * `inFlight` is a `useRef` rather than `mutation.isPending`: two taps in the same tick both run
 * before React re-renders, so state has not caught up and the second would issue a second request.
 * The server refuses it — `state` is the EF concurrency token, so the loser gets
 * `409 illegal_transition` — but the button should not be sending it, and a 409 the garage never
 * caused is a confusing thing to render. 1.5's lesson, sixth outing.
 */
export function useDeclarationAction(
  declarationId: string,
  action: DeclarationAction,
): UseDeclarationActionResult {
  const queryClient = useQueryClient()
  const [failed, setFailed] = useState<string | null>(null)
  const inFlight = useRef(false)

  const mutation = useMutation({
    mutationFn: () => RUNNERS[action](declarationId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: garageKeys.detail(declarationId) })
      await queryClient.invalidateQueries({ queryKey: garageKeys.lists() })
    },
  })

  function run() {
    if (inFlight.current) return
    inFlight.current = true
    setFailed(null)

    mutation.mutate(undefined, {
      onSettled: () => {
        // Released either way, unlike `useArrived`: a transition that succeeded moves the
        // declaration to a state where this button is not rendered at all, so the latch has nothing
        // left to guard — and a failure must be retryable.
        inFlight.current = false
      },
      onError: (error) => {
        setFailed(describe(error, action))
      },
    })
  }

  return { run, pending: mutation.isPending, failed }
}

const RUNNERS: Record<
  DeclarationAction,
  (declarationId: string) => Promise<{ state: DeclarationState }>
> = {
  submit: submitDeclaration,
  'start-repairs': startRepairs,
  'submit-repair-docs': submitRepairDocs,
}

const VERBS: Record<DeclarationAction, string> = {
  submit: 'submitted',
  'start-repairs': 'started',
  'submit-repair-docs': 'sent',
}

function describe(error: unknown, action: DeclarationAction): string {
  const verb = VERBS[action]

  if (error instanceof ApiError && error.status === 409) {
    // Slice 5.1's one 409 that is not "somebody moved this first": the repair documents are missing.
    // The button is disabled until the list says otherwise, so reaching this means the list was a
    // moment behind — and telling that garage the declaration "has already moved on" would send it
    // looking for a state change that never happened.
    if (error.body.includes('repair_documents_required')) {
      return 'Add at least one repair document before sending these to AXA.'
    }

    // The concurrency token, or a genuinely out-of-order request. Either way somebody else moved
    // this declaration first, and the honest answer is to say so and let the screen refetch.
    return `This declaration has already moved on — reopen it to see where it is now.`
  }

  if (error instanceof ApiError) {
    return `Not ${verb} (${error.status}). Check the connection and try again.`
  }

  return `Not ${verb}. Check the connection and try again.`
}
