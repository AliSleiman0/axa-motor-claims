import { useRef, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../api/client'
import { expertKeys, postArrival } from './api'
import { GeolocationError, requestPosition, type GeolocationFailure } from './geolocation'

export interface UseArrivedResult {
  /** Presses Arrived. A no-op once the assignment has arrived or while a press is in flight. */
  press: () => void
  pending: boolean
  arrived: boolean
  arrivedAt: string | null
  /** Set when the browser would not give a position — E2 renders the blocking explanation. */
  blockedBy: GeolocationFailure | null
  /** Set when the request itself failed. Never both this and `blockedBy`. */
  failed: string | null
}

/**
 * Everything the Arrived button does (design.md §5.1's Arrived row).
 *
 * The order matters: the position is fetched **first**, and if the browser refuses one, no request
 * is made at all — §5.1 says arrival without location is not sent, because the BRD requires all
 * three values and a half-arrival in NEXT3 is worse than none.
 *
 * Note what this hook deliberately does not do: it gates nothing else. §5.1's recorded
 * interpretation is that Arrived is not a precondition for capture — a roadside expert whose GPS is
 * slow must still be able to photograph the car (slice 2.5 inherits this).
 */
export function useArrived(assignmentId: string, arrivedAt: string | null): UseArrivedResult {
  const queryClient = useQueryClient()
  const [blockedBy, setBlockedBy] = useState<GeolocationFailure | null>(null)
  // A latch, not `mutation.isPending`: two taps in the same tick both run before React re-renders,
  // so state would not have caught up and the second tap would issue a second request. The server
  // refuses it anyway (the arrival is claimed in the UPDATE's WHERE clause), but the button should
  // not be sending it.
  const inFlight = useRef(false)

  const mutation = useMutation({
    mutationFn: async () => postArrival(assignmentId, await requestPosition()),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: expertKeys.detail(assignmentId) })
      // `lists()` for the same reason as `useRefreshAfterCapture` — a search may be active.
      await queryClient.invalidateQueries({ queryKey: expertKeys.lists() })
    },
  })

  // `mutation.isSuccess` as well as the prop: the arrival is a fact the moment the server says 200,
  // and the detail query has not refetched yet.
  const arrived = arrivedAt !== null || mutation.isSuccess

  function press() {
    if (arrived || inFlight.current) return
    inFlight.current = true
    setBlockedBy(null)
    mutation.mutate(undefined, {
      onError: (error) => {
        // Released only on failure: a successful arrival must never be pressable again.
        inFlight.current = false
        if (error instanceof GeolocationError) setBlockedBy(error.reason)
      },
    })
  }

  const geolocationFailed = mutation.error instanceof GeolocationError
  return {
    press,
    pending: mutation.isPending,
    arrived,
    arrivedAt: mutation.data?.arrivedAt ?? arrivedAt,
    blockedBy,
    failed:
      mutation.error && !geolocationFailed
        ? mutation.error instanceof ApiError
          ? `Arrival was not sent (${mutation.error.status}). Try again.`
          : 'Arrival was not sent. Check the connection and try again.'
        : null,
  }
}
