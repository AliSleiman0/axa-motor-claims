import type {
  InputHTMLAttributes,
  ReactNode,
  SelectHTMLAttributes,
  TextareaHTMLAttributes,
} from 'react'

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

export interface SelectFieldProps extends Omit<SelectHTMLAttributes<HTMLSelectElement>, 'id'> {
  id: string
  label: string
  error?: string | null
  hint?: ReactNode
  /** The values the server will accept, in the order it lists them. */
  options: readonly string[]
  /** Shown first and worth nothing — a native select with no empty option pre-picks option one. */
  placeholder?: string
}

/**
 * A native `<select>` (slice 5.2, B2's insurance type).
 *
 * Native rather than a custom listbox: this is one of two screens where a wrong choice sends the
 * quotation to the wrong AXA desk, and a native control is the one every browser, screen reader and
 * phone keyboard already agrees about.
 *
 * **The options are always passed in, never held here.** They come from `Broker:InsuranceTypes` (#14)
 * over the wire; a list in this file would be client data outside the placeholder config, which
 * CLAUDE.md calls a bug wherever it appears.
 */
export function SelectField({
  id,
  label,
  error,
  hint,
  options,
  placeholder,
  className,
  ...rest
}: SelectFieldProps) {
  return (
    <FieldShell id={id} label={label} error={error} hint={hint}>
      <select
        id={id}
        className={['field__control', className ?? ''].filter(Boolean).join(' ')}
        aria-invalid={error ? true : undefined}
        {...rest}
      >
        {placeholder ? <option value="">{placeholder}</option> : null}
        {options.map((option) => (
          <option key={option} value={option}>
            {option}
          </option>
        ))}
      </select>
    </FieldShell>
  )
}

/**
 * A date, mono and tabular (slice 5.2, B2's effective date).
 *
 * `type="date"` so the value on the wire is always `yyyy-MM-dd` whatever the browser draws — the
 * server binds a `DateOnly`, and a locale-formatted string would be 03/09 in one place and 09/03 in
 * another on a policy's start date.
 */
export function DateField({ id, label, error, hint, className, ...rest }: TextFieldProps) {
  return (
    <TextField
      id={id}
      label={label}
      error={error}
      hint={hint}
      type="date"
      className={['field__control--mono', className ?? ''].filter(Boolean).join(' ')}
      {...rest}
    />
  )
}

/**
 * An amount, mono and tabular (slice 5.2, B2's car value and estimated premium).
 *
 * **No currency symbol, and that is a decision rather than an omission.** The BRD names both fields
 * and names no currency; one value per deployment or one per insurance type is unanswered (#47).
 * Drawing a symbol would put a client literal in TypeScript, and drawing the wrong one on a policy
 * amount is worse than drawing none — so the field shows the number and the question stays open.
 */
export function MoneyField({ id, label, error, hint, className, ...rest }: TextFieldProps) {
  return (
    <TextField
      id={id}
      label={label}
      error={error}
      hint={hint}
      type="number"
      inputMode="decimal"
      min={0}
      step="0.01"
      className={['field__control--mono', className ?? ''].filter(Boolean).join(' ')}
      {...rest}
    />
  )
}
