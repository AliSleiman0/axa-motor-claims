import type { DeclarationState } from './api'

/**
 * §5.2's six states in the words a garage reads, rather than the wire values.
 *
 * A map keyed by the state union rather than a `switch`, so TypeScript fails the build if a seventh
 * state is ever added to `DeclarationState` and a screen forgets it — the same reason the server
 * keeps `DeclarationStates.All` beside its constants.
 *
 * `repair_docs_submitted` is here because the state exists in the schema; slice 5.1 builds the G4
 * step that reaches it.
 */
export const STATE_LABELS: Record<DeclarationState, string> = {
  draft: 'Draft',
  submitted: 'Waiting for AXA',
  approved: 'Approved',
  rejected: 'Not accepted',
  repairs_in_progress: 'Repairs in progress',
  repair_docs_submitted: 'Repair documents sent',
}
