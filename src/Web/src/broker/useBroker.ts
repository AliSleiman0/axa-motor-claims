import { useRef, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/client'
import {
  type BrokerActionResult,
  brokerKeys,
  fetchBrokerConfig,
  getRequest,
  listRequestDocuments,
  listRequests,
  resendRequest,
  sendRequest,
  submitRequest,
} from './api'

/**
 * B1's worklist (§5.3). The server orders newest-first and the screen never re-sorts — two orderings
 * that can disagree is a worse bug than either, which is O1's rule and the same one here.
 */
export function useRequests() {
  return useQuery({
    queryKey: brokerKeys.list(),
    queryFn: ({ signal }) => listRequests(signal),
    placeholderData: keepPreviousData,
  })
}

/** #14's list, cached for the session — it changes when configuration does, not while typing. */
export function useBrokerConfig() {
  return useQuery({
    queryKey: ['broker', 'config'] as const,
    queryFn: ({ signal }) => fetchBrokerConfig(signal),
    staleTime: Infinity,
  })
}

export function useRequest(requestId: string) {
  return useQuery({
    queryKey: brokerKeys.detail(requestId),
    queryFn: ({ signal }) => getRequest(requestId, signal),
  })
}

export function useRequestDocuments(requestId: string) {
  return useQuery({
    queryKey: brokerKeys.documents(requestId),
    queryFn: ({ signal }) => listRequestDocuments(requestId, signal),
  })
}

/**
 * What B2 must refresh after a capture lands — the documents, and the list, whose document count is
 * on screen. Here rather than in `media/`, for `useRefreshAfterCapture`'s reason: the capture hook
 * must not learn broker query keys or it stops being reusable by §5.3's public page.
 */
export function useRefreshAfterCapture(requestId: string) {
  const queryClient = useQueryClient()
  return () => {
    void queryClient.invalidateQueries({ queryKey: brokerKeys.documents(requestId) })
    void queryClient.invalidateQueries({ queryKey: brokerKeys.lists() })
  }
}

export type BrokerAction = 'submit' | 'resend' | 'send'

export interface UseBrokerActionResult {
  run: () => void
  pending: boolean
  failed: string | null
  /** The server's answer, kept so the outcome state can name the recipient (the B2 artboard's rule). */
  result: BrokerActionResult | null
}

/**
 * Submit or Resend, behind the house latch.
 *
 * `inFlight` is a `useRef` rather than `mutation.isPending`: two taps in the same tick both run
 * before React re-renders. The server refuses the second either way — the submit on `state`, the
 * resend on its claim over `emailed_at` — but the button should not be sending it. 1.5's lesson,
 * seventh outing.
 */
export function useBrokerAction(requestId: string, action: BrokerAction): UseBrokerActionResult {
  const queryClient = useQueryClient()
  const [failed, setFailed] = useState<string | null>(null)
  const [result, setResult] = useState<BrokerActionResult | null>(null)
  const inFlight = useRef(false)

  const mutation = useMutation({
    mutationFn: () => RUNNERS[action](requestId),
    onSuccess: async (answer) => {
      setResult(answer)
      await queryClient.invalidateQueries({ queryKey: brokerKeys.detail(requestId) })
      await queryClient.invalidateQueries({ queryKey: brokerKeys.lists() })
    },
  })

  function run() {
    if (inFlight.current) return
    inFlight.current = true
    setFailed(null)

    mutation.mutate(undefined, {
      onSettled: () => {
        inFlight.current = false
      },
      onError: (error) => {
        setFailed(describe(error, action))
      },
    })
  }

  return { run, pending: mutation.isPending, failed, result }
}

const RUNNERS: Record<BrokerAction, (requestId: string) => Promise<BrokerActionResult>> = {
  submit: submitRequest,
  resend: resendRequest,
  send: sendRequest,
}

const VERBS: Record<BrokerAction, string> = {
  submit: 'submitted',
  resend: 'sent',
  send: 'sent',
}

/**
 * The server's refusal codes, turned into something a broker with a customer in front of them can act
 * on. A bare "(409)" is what 4.1's two codes and 5.1's rendered as for a week before somebody noticed.
 */
const EXPLANATIONS: Record<string, string> = {
  unknown_insurance_type:
    'That insurance type is no longer offered. Reopen the request and choose one from the list.',
  invalid_amount: 'The car value and the estimated premium must both be more than zero.',
  incomplete_request: 'All six details are needed before this can be sent.',
  already_emailed: 'This request has already been emailed to AXA.',
  not_submitted: 'This request is not waiting to be emailed.',
  attachments_unavailable:
    'The attached documents are no longer stored, so this cannot be sent again. Create a new request.',
  illegal_transition: 'This request has already moved on — reopen it to see where it is now.',
}

function describe(error: unknown, action: BrokerAction): string {
  if (error instanceof ApiError) {
    for (const [code, sentence] of Object.entries(EXPLANATIONS)) {
      if (error.body.includes(code)) return sentence
    }

    return `Not ${VERBS[action]} (${error.status}). Check the connection and try again.`
  }

  return `Not ${VERBS[action]}. Check the connection and try again.`
}
