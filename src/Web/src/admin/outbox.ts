import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'

/**
 * A2's data (design.md §5.4, slice 6.2) — the admin module's first api file.
 *
 * **On react-query rather than A1's `useEffect`.** The two profile screens are the only ones in the
 * app that fetch by hand, and `scope-decisions.md` records that as a state of affairs to be left
 * alone until a slice touches them, not as the pattern. This screen refetches on a timer, invalidates
 * two keys from a mutation and drives a count in the chrome; hand-rolling that would be re-inventing
 * the library the other four modules already use.
 */

/** One queued push, exactly as `OutboxAdminRowDto` sends it. */
export interface OutboxRow {
  id: string
  /**
   * Raw: `upload_document`, `update_arrival`, `push_approval`. Rendered in mono and never
   * prettified — an admin reading this to a developer needs the string the database holds.
   */
  operation: string
  visaNo: string
  /** `failed` or `pending`. `processing` is never listed — the lease returns those rows by itself. */
  status: string
  attempts: number
  lastError: string | null
  createdAt: string
  /** Null until the row has been claimed at least once; the screen shows an em dash. */
  lastAttemptAt: string | null
  nextRetryAt: string
}

/**
 * The two numbers behind the nav badge (slice 7.2 added `overdue`).
 *
 * They are counted separately server-side and summed here, which is the point rather than an
 * accident. `failed` is a row that has given up and will sit there for ever. `overdue` is a
 * `pending` row whose moment came and went and which nothing is claiming — the signature of a
 * stopped worker, or of a backlog draining slower than it fills. Long-`pending` rows are on the
 * *list* and in neither number: one is working through its backoff and clears itself, and a badge
 * that rose and fell with the retry schedule is an alarm nobody would trust (slice 6.2's argument,
 * kept).
 */
export interface OutboxFailedCount {
  failed: number
  overdue: number
}

const BASE = '/api/admin/outbox'

export const outboxKeys = {
  all: ['admin', 'outbox'] as const,
  lists: () => [...outboxKeys.all, 'list'] as const,
  list: () => [...outboxKeys.lists(), ''] as const,
  /** The nav badge. Its own key, because it survives on screens the list is not mounted on. */
  count: () => [...outboxKeys.all, 'count'] as const,
}

export function listOutbox(signal?: AbortSignal): Promise<OutboxRow[]> {
  return api<OutboxRow[]>(BASE, { signal })
}

export function fetchOutboxFailedCount(signal?: AbortSignal): Promise<OutboxFailedCount> {
  return api<OutboxFailedCount>(`${BASE}/count`, { signal })
}

export function retryPush(messageId: string): Promise<void> {
  return api<void>(`${BASE}/${messageId}/retry`, { method: 'POST' })
}

export function retryAllPushes(): Promise<{ retried: number }> {
  return api<{ retried: number }>(`${BASE}/retry-all`, { method: 'POST' })
}

/**
 * Mirrors `Outbox:PollSeconds`, the interval the worker itself drains on — so the screen and the
 * queue move at the same rate and a row that clears is gone within one tick of clearing.
 *
 * An engineering constant, not client data, so it is a literal here rather than a placeholder key:
 * Appendix A's rule is about values AXA owns (insurance types, recipients, thresholds, NEXT3 codes),
 * and nothing about how often a browser polls is theirs to answer. It is named rather than inlined
 * twice so the two queries cannot drift apart.
 */
const POLL_MS = 30_000

export function useOutboxList() {
  return useQuery({
    queryKey: outboxKeys.list(),
    queryFn: ({ signal }) => listOutbox(signal),
    refetchInterval: POLL_MS,
  })
}

/**
 * The nav badge. Called from `AdminLayout`, so it runs on every admin screen — which is the point:
 * nobody opens A2 on a hunch, so if a failure never announces itself it is never read.
 */
export function useOutboxFailedCount() {
  return useQuery({
    queryKey: outboxKeys.count(),
    queryFn: ({ signal }) => fetchOutboxFailedCount(signal),
    refetchInterval: POLL_MS,
  })
}

/**
 * Both retries invalidate **both** keys. The list obviously changes; the badge changes too, and it is
 * the one that is read from the other four admin screens, so leaving it stale would keep a number in
 * the chrome contradicting a screen the admin has just fixed.
 */
export function useRetryPush() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (messageId: string) => retryPush(messageId),
    onSuccess: () => invalidate(queryClient),
  })
}

export function useRetryAllPushes() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: () => retryAllPushes(),
    onSuccess: () => invalidate(queryClient),
  })
}

async function invalidate(queryClient: ReturnType<typeof useQueryClient>) {
  await queryClient.invalidateQueries({ queryKey: outboxKeys.lists() })
  await queryClient.invalidateQueries({ queryKey: outboxKeys.count() })
}
