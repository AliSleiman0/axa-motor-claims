import { useRef, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/client'
import { renderApprovalToFile } from '../media/approval/approval'
import type { SvgRenderer } from '../media/png'
import { uploadDocument, describeUploadError } from '../media/upload'
import {
  approveDeclaration,
  getMe,
  meKey,
  getOfficerDeclaration,
  listInbox,
  listOfficerDocuments,
  officerDocumentsPath,
  officerKeys,
  rejectDeclaration,
  searchClaims,
  type ClaimSearchResult,
} from './api'

/** O1's inbox. The server orders oldest-first — an officer works a queue — so the screen never sorts. */
export function useInbox(state?: string) {
  return useQuery({
    queryKey: officerKeys.list(state),
    queryFn: ({ signal }) => listInbox(state, signal),
    placeholderData: keepPreviousData,
  })
}

export function useOfficerDeclaration(declarationId: string) {
  return useQuery({
    queryKey: officerKeys.detail(declarationId),
    queryFn: ({ signal }) => getOfficerDeclaration(declarationId, signal),
  })
}

export function useOfficerDocuments(declarationId: string) {
  return useQuery({
    queryKey: officerKeys.documents(declarationId),
    queryFn: ({ signal }) => listOfficerDocuments(declarationId, signal),
  })
}

export interface UseVisaSearchResult {
  results: ClaimSearchResult[] | null
  pending: boolean
  failed: string | null
  run: (plateNo: string, visaNo: string) => void
}

/**
 * O2's visa lookup (§5.2's officer-review row).
 *
 * A mutation rather than a query, because it runs when the officer presses Search — not on every
 * keystroke. §6.1 refuses a search with neither term, and this refuses to send one.
 */
export function useVisaSearch(): UseVisaSearchResult {
  const [results, setResults] = useState<ClaimSearchResult[] | null>(null)
  const [failed, setFailed] = useState<string | null>(null)

  const mutation = useMutation({
    mutationFn: ({ plateNo, visaNo }: { plateNo: string; visaNo: string }) =>
      searchClaims(plateNo, visaNo),
  })

  function run(plateNo: string, visaNo: string) {
    const plate = plateNo.trim()
    const visa = visaNo.trim()
    setFailed(null)

    if (!plate && !visa) {
      // §6.1: "neither term supplied returns empty, never every claim". The server refuses it too;
      // this stops the screen producing a request that cannot succeed.
      setFailed('Enter a plate number or a visa number to search.')
      return
    }

    mutation.mutate(
      { plateNo: plate, visaNo: visa },
      {
        onSuccess: (found) => {
          setResults(found)
        },
        onError: (error) => {
          setResults(null)
          setFailed(
            error instanceof ApiError && error.status === 503
              ? 'NEXT3 is unreachable, so claims cannot be searched right now. Try again shortly.'
              : `The search failed${error instanceof ApiError ? ` (${error.status})` : ''}.`,
          )
        },
      },
    )
  }

  return { results, pending: mutation.isPending, failed, run }
}

export interface UseDecisionOptions {
  /** Injected by tests; production takes the canvas renderer inside `renderApprovalToFile`. */
  render?: SvgRenderer
  /** Injected so a test can assert the stamp without freezing the clock. */
  now?: () => Date
}

/**
 * Which of approve's three steps is running (pass 3's O2States: "Rendering decision… Uploading…
 * Approving…"). Null while idle, and null throughout a rejection — a rejection is one call.
 */
export type DecisionStep = 'rendering' | 'uploading' | 'approving'

export interface UseDecisionResult {
  approve: (visaNo: string, comment: string) => void
  reject: (comment: string) => void
  pending: boolean
  /**
   * Named so the officer knows which step they are on — and it is worth naming precisely because the
   * steps are not equivalent: a failure at *rendering* or *uploading* leaves the declaration
   * untouched, while the third one is the irreversible act. Additive state beside `pending`; the
   * ordering, the single latch and the server's `409 approval_image_required` are unchanged.
   */
  step: DecisionStep | null
  failed: string | null
}

/**
 * O2's Approve and Reject (design.md §5.2), behind **one** latch — the two are alternatives, and a
 * screen that let both be pressed at once would be racing its own user.
 *
 * **Approve is three steps in a fixed order, and the order is the point.** #18's decision image is
 * rendered and uploaded *before* the approve call, so a render or an upload failure stops with the
 * declaration untouched — §5.2's "a render failure aborts the approval cleanly". The server enforces
 * the same rule from its side (`409 approval_image_required`), so the two agree.
 *
 * The upload goes through `uploadDocument` directly rather than through `useCapture`. `useCapture` is
 * a select→confirm→send machine whose whole purpose is to make a person look at a file (§7.2's E4
 * screen); Approve is one action over content the officer composed on this screen a moment ago, and
 * threading it through a confirm step would put a second button in the middle of a decision. What the
 * fixed 1600×1200 render satisfies is the **server's** §7.2 floor, which applies either way.
 */
export function useDecision(
  declarationId: string,
  { render, now = () => new Date() }: UseDecisionOptions = {},
): UseDecisionResult {
  const queryClient = useQueryClient()
  const [failed, setFailed] = useState<string | null>(null)
  const [step, setStep] = useState<DecisionStep | null>(null)
  // 1.5's lesson: two clicks in the same tick both read `isPending` as false, and the second would
  // send a second decision.
  const inFlight = useRef(false)

  const mutation = useMutation({
    mutationFn: async (decision: { kind: 'approve' | 'reject'; visaNo: string; comment: string }) => {
      if (decision.kind === 'reject') {
        return rejectDeclaration(declarationId, decision.comment)
      }

      setStep('rendering')

      const detail = await queryClient.ensureQueryData({
        queryKey: officerKeys.detail(declarationId),
        queryFn: ({ signal }) => getOfficerDeclaration(declarationId, signal),
      })

      // From `/auth/me`, not from a claim in the token: the JWT carries `sub` and `role` and no
      // name, and inventing one for an artifact that lands in AXA's claim folder is not on.
      const me = await queryClient.ensureQueryData({
        // `meKey`, not a second literal: `AppHeader` fetches the same endpoint, and two keys for one
        // endpoint could disagree about who is signed in — on the artifact that reaches AXA.
        queryKey: meKey,
        queryFn: ({ signal }) => getMe(signal),
      })

      const file = await renderApprovalToFile(
        {
          declarationId,
          plateNo: detail.plateNo,
          visaNo: decision.visaNo,
          officerName: me.displayName,
          preparedAt: now(),
          comments: decision.comment,
        },
        render,
      )

      setStep('uploading')
      await uploadDocument({
        path: officerDocumentsPath(declarationId),
        bucket: 'approval_image',
        origin: 'captured',
        file,
      })

      setStep('approving')
      return approveDeclaration(declarationId, decision.visaNo, decision.comment)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: officerKeys.detail(declarationId) })
      await queryClient.invalidateQueries({ queryKey: officerKeys.documents(declarationId) })
      await queryClient.invalidateQueries({ queryKey: officerKeys.lists() })
    },
  })

  function start(kind: 'approve' | 'reject', visaNo: string, comment: string) {
    if (inFlight.current) return

    if (kind === 'approve' && visaNo.trim().length === 0) {
      setFailed('Find the claim in NEXT3 and use its visa number before approving.')
      return
    }

    inFlight.current = true
    setFailed(null)

    mutation.mutate(
      { kind, visaNo: visaNo.trim(), comment },
      {
        onSettled: () => {
          inFlight.current = false
          // Cleared on failure as well as success: the button must not go on claiming a step that
          // stopped, and `failed` is what says what happened.
          setStep(null)
        },
        onError: (error) => {
          setFailed(describeDecision(error, kind))
        },
      },
    )
  }

  return {
    approve: (visaNo, comment) => {
      start('approve', visaNo, comment)
    },
    reject: (comment) => {
      start('reject', '', comment)
    },
    pending: mutation.isPending,
    step,
    failed,
  }
}

function describeDecision(error: unknown, kind: 'approve' | 'reject'): string {
  if (error instanceof ApiError && error.status === 422) {
    // The server asked NEXT3 and NEXT3 does not know this visa. #16: the officer creates it in NEXT3
    // and searches again — there is no create-visa API and this release does not invent one.
    return 'NEXT3 does not have a claim under that visa number. Create the visa in NEXT3, then search again.'
  }

  if (error instanceof ApiError && error.status === 503) {
    return 'NEXT3 is unreachable, so the claim cannot be confirmed right now. Try again shortly.'
  }

  if (error instanceof ApiError && error.status === 409) {
    return 'This declaration has already been decided — reopen it to see where it is now.'
  }

  // An upload refusal comes back with the media module's own `{ error: code }` body, so it is worth
  // asking that translator first: "the approval image was too small" is actionable and "(400)" is not.
  const uploadProblem = error instanceof ApiError ? describeUploadError(error) : null
  if (uploadProblem && error instanceof ApiError && error.status < 500 && kind === 'approve') {
    return uploadProblem
  }

  return `The decision was not recorded${
    error instanceof ApiError ? ` (${error.status})` : ''
  }. Check the connection and try again.`
}
