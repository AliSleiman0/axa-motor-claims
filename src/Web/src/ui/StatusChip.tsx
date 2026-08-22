import { DECLARATION_TONES, PROFILE_STATUS_LABELS, PROFILE_STATUS_TONES, type Tone } from './tones'

export interface StatusChipProps {
  label: string
  tone?: Tone
  size?: 'md' | 'sm'
}

/**
 * A status, rendered as tint + border + text (pass 1).
 *
 * All three together, never a bare fill: the chip has to survive greyscale and a colour-blind reader,
 * and these are the labels an officer scans a queue by. **Never a button** — a status is what
 * happened, not something to press.
 */
export function StatusChip({ label, tone, size = 'md' }: StatusChipProps) {
  const resolved = tone ?? DECLARATION_TONES[label] ?? 'neutral'
  return (
    <span className={`chip chip--${resolved}${size === 'sm' ? ' chip--sm' : ''}`}>{label}</span>
  )
}

/** The same chip for §4's `app_user.status`, so the admin list reads like the rest of the product. */
export function ProfileStatusChip({ status }: { status: string }) {
  return (
    <StatusChip
      label={PROFILE_STATUS_LABELS[status] ?? status}
      tone={PROFILE_STATUS_TONES[status] ?? 'neutral'}
      size="sm"
    />
  )
}
