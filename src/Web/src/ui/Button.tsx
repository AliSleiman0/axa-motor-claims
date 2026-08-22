import type { ButtonHTMLAttributes, ReactNode } from 'react'

export type ButtonVariant = 'primary' | 'secondary' | 'destructive' | 'link' | 'icon'

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  /** Full width — the phone shells' single action at the foot of a panel. */
  block?: boolean
  children: ReactNode
}

/**
 * The one button (pass 1's `Button/*`).
 *
 * **Pending never spins.** Every caller in this codebase already swaps the label for its gerund and
 * disables the control — `Arrived`/`Sending…`, `Approve`/`Working…`, `Submit to AXA`/`Sending…` — and
 * that is deliberate rather than incidental: a spinner claims progress it cannot measure, while a
 * changed label says exactly which of the two things is happening. So there is no `loading` prop
 * here; `disabled` and the caller's own label are the whole mechanism, and nothing had to change at
 * a single call site to adopt this component.
 *
 * `type` defaults to `button`. A bare `<button>` inside a `<form>` submits it, which on G2 would
 * mean a second declaration for the same car.
 */
export function Button({
  variant = 'secondary',
  block = false,
  type = 'button',
  className,
  children,
  ...rest
}: ButtonProps) {
  const classes = ['btn', `btn--${variant}`, block ? 'btn--block' : '', className ?? '']
    .filter(Boolean)
    .join(' ')

  return (
    <button type={type} className={classes} {...rest}>
      {children}
    </button>
  )
}
