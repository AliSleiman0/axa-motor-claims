import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'

/**
 * The desktop worklist (pass 1's `WorklistRow`): a real table, `--row-min` tall, whole row hoverable.
 *
 * A `<table>` and not a grid of divs — an officer comparing six submitted declarations down a column
 * is doing exactly what a table is for, and the header cells are what let a screen reader say which
 * column a value came from.
 */
export function Worklist({ headers, children }: { headers: ReactNode[]; children: ReactNode }) {
  return (
    <div className="worklist__scroll">
      <table className="worklist">
        <thead>
          <tr>
            {headers.map((header, index) => (
              <th key={index} scope="col">
                {header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  )
}

export interface WorklistCardProps {
  to: string
  /** The reference the user recognises the row by — a visa number or a plate. Mono. */
  reference: string
  status?: ReactNode
  /** Short label/value pairs under the head. Each value is its own element. */
  meta: { label: string; value: ReactNode }[]
}

/**
 * The phone worklist (pass 1's `WorklistCard`). One card per row, the reference leading, a 56 px
 * minimum as the floor rather than the target.
 *
 * **The link wraps the reference, not the card** — and the card is still tappable edge to edge,
 * because `.worklist-card__link::after` stretches an invisible overlay across it. Making the whole
 * card an `<a>` was the obvious version and it is wrong twice over: the link's accessible name
 * becomes the entire card, so a screen reader reads six label/value pairs before saying which claim
 * this is, and the row's identity stops being addressable at all. A stretched overlay gives the
 * gloved thumb the whole card and leaves the announced name as "PLACEHOLDER-VISA-0001".
 */
export function WorklistCard({ to, reference, status, meta }: WorklistCardProps) {
  return (
    <div className="worklist-card">
      <div className="worklist-card__head">
        <Link className="worklist-card__link worklist-card__ref" to={to}>
          {reference}
        </Link>
        {status}
      </div>
      <div className="worklist-card__meta">
        {meta.map((entry) => (
          <span key={entry.label} className="worklist-card__pair">
            <span className="worklist-card__label">{entry.label}</span>
            <span>{entry.value}</span>
          </span>
        ))}
      </div>
    </div>
  )
}
