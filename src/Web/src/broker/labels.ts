import type { BrokerRequestState } from './api'

/**
 * §5.3's seven states in the words a broker reads, rather than the wire values.
 *
 * Keyed by the state union rather than a `switch`, so TypeScript fails the build if an eighth state
 * is added and a screen forgets it — `garage/labels.ts`'s reason, and the server's for keeping
 * `BrokerRequestStates.All` beside its constants.
 *
 * `expired` is in the map like any other, even though **nothing ever writes it**: the server computes
 * it in B1's projection from the token's own expiry (§9.1 keeps that the authority), so the screen
 * receives it exactly as it receives the six stored ones.
 */
export const BROKER_STATE_LABELS: Record<BrokerRequestState, string> = {
  draft: 'Draft',
  submitted: 'Submitted',
  link_issued: 'Link issued',
  customer_in_progress: 'Customer in progress',
  ready_to_send: 'Ready to send',
  sent: 'Sent',
  expired: 'Expired',
}
