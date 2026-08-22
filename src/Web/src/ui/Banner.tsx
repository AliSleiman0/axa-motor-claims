import type { ReactNode } from 'react'

export interface BannerProps {
  children: ReactNode
  /** `alert` (red) is the default; `warn` is amber, `info` the accent tint. */
  intent?: 'alert' | 'warn' | 'info'
}

/**
 * Something did not happen (pass 1's `AlertBanner`). `role="alert"`, filled and bordered on its
 * intent colour, so it is distinct from a `StatusBanner` at arm's length.
 *
 * Red names the next action; **amber means "wait, it may fix itself"** (pass 3). A NEXT3 outage is
 * amber because it will pass; a rejection or a 400 is red because somebody has to do something.
 *
 * The child is always a whole sentence, never a code — the HTTP status belongs inside it, in
 * parentheses ("Could not load the inbox (503)."), which is what every call site already writes.
 */
export function AlertBanner({ children, intent = 'alert' }: BannerProps) {
  return (
    <div className={`banner banner--${intent}`} role="alert">
      <span className="banner__icon" aria-hidden="true">
        {intent === 'alert' ? '!' : intent === 'warn' ? '!' : 'i'}
      </span>
      <span>{children}</span>
    </div>
  )
}

/**
 * Where things stand (pass 1's `StatusBanner`). `role="status"`, a quiet neutral strip — nothing has
 * gone wrong, so it must not look like it has.
 */
export function StatusBanner({ children }: { children: ReactNode }) {
  return (
    <div className="banner banner--status" role="status">
      <span className="banner__icon" aria-hidden="true">
        i
      </span>
      <span>{children}</span>
    </div>
  )
}
