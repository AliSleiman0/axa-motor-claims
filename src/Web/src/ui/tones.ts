/**
 * The semantic tones, and the two label→tone maps.
 *
 * A `.ts` file rather than living beside `StatusChip.tsx`, for `push/copy.ts`'s reason:
 * `react-refresh/only-export-components` — an error here via `--max-warnings 0` — allows a `.tsx`
 * file to export components and nothing else.
 */

export type Tone = 'neutral' | 'warn' | 'ok' | 'danger' | 'accent' | 'ink'

/**
 * §5.2's six declaration states, keyed by the label the user reads rather than by the wire value, so
 * this file never has to import `garage/api`'s `DeclarationState` — `ui/` may not reach into a role
 * module, and `STATE_LABELS` stays the single place the words are decided.
 *
 * The tones follow the state machine's shape: Draft is nothing yet, Waiting is amber ("wait, it may
 * fix itself"), Approved is the good terminal branch, Not accepted the bad one, Repairs in progress
 * is live work, and Repair documents sent is done and quiet.
 */
export const DECLARATION_TONES: Record<string, Tone> = {
  Draft: 'neutral',
  'Waiting for AXA': 'warn',
  Approved: 'ok',
  'Not accepted': 'danger',
  'Repairs in progress': 'accent',
  'Repair documents sent': 'ink',
}

/** §4's `app_user.status` values, as the admin list shows them. */
export const PROFILE_STATUS_LABELS: Record<string, string> = {
  invited: 'Invited',
  active: 'Active',
  inactive: 'Inactive',
}

export const PROFILE_STATUS_TONES: Record<string, Tone> = {
  invited: 'warn',
  active: 'ok',
  inactive: 'neutral',
}
