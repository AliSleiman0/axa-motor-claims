import type { InputHTMLAttributes, ReactNode, TextareaHTMLAttributes } from 'react'

interface FieldShellProps {
  id: string
  label: string
  /** A plain sentence under the control. The HTTP status goes inside it, in parentheses. */
  error?: string | null
  hint?: ReactNode
  children: ReactNode
}

/**
 * Label above, control, then the error sentence — the shape every field in pass 1 shares.
 *
 * The label is always visible. A placeholder is not a label: it disappears the moment somebody types,
 * which is precisely when a garage entering a plate number at a counter looks up again.
 */
function FieldShell({ id, label, error, hint, children }: FieldShellProps) {
  return (
    <div className="field">
      <label className="field__label" htmlFor={id}>
        {label}
      </label>
      {children}
      {hint ? <span className="caption">{hint}</span> : null}
      {error ? (
        <span className="field__error" role="alert">
          {error}
        </span>
      ) : null}
    </div>
  )
}

export interface TextFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  id: string
  label: string
  error?: string | null
  hint?: ReactNode
}

export function TextField({ id, label, error, hint, className, ...rest }: TextFieldProps) {
  return (
    <FieldShell id={id} label={label} error={error} hint={hint}>
      <input
        id={id}
        className={['field__control', className ?? ''].filter(Boolean).join(' ')}
        aria-invalid={error ? true : undefined}
        {...rest}
      />
    </FieldShell>
  )
}

/**
 * E.164, mono and tabular, numeric keypad. `inputMode` rather than `type="tel"` alone so the `+` is
 * still typeable — every phone in this system is `+999…` and a keypad without it is unusable.
 */
export function PhoneField({ id, label, error, hint, className, ...rest }: TextFieldProps) {
  return (
    <TextField
      id={id}
      label={label}
      error={error}
      hint={hint}
      type="tel"
      inputMode="tel"
      autoComplete="tel"
      className={['field__control--mono', className ?? ''].filter(Boolean).join(' ')}
      {...rest}
    />
  )
}

/**
 * The search box. `type="search"`, and **the caller keeps it mounted across every branch** — E1
 * already does, because a field unmounted by a loading state takes the caret and the focus with it,
 * which is invisible in a test and immediately fatal on a phone.
 */
export function SearchField({ id, label, error, hint, className, ...rest }: TextFieldProps) {
  return (
    <TextField
      id={id}
      label={label}
      error={error}
      hint={hint}
      type="search"
      className={className}
      {...rest}
    />
  )
}

export interface TextAreaProps extends Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'id'> {
  id: string
  label: string
  error?: string | null
  hint?: ReactNode
}

/** Three rows minimum, grows. The officer's decision comment is the one that runs long. */
export function TextArea({ id, label, error, hint, className, rows = 3, ...rest }: TextAreaProps) {
  return (
    <FieldShell id={id} label={label} error={error} hint={hint}>
      <textarea
        id={id}
        rows={rows}
        className={['field__control', className ?? ''].filter(Boolean).join(' ')}
        aria-invalid={error ? true : undefined}
        {...rest}
      />
    </FieldShell>
  )
}
