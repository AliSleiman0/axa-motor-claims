import type { ReactNode } from 'react'

export interface DetailRow {
  label: string
  value: ReactNode
  /** Reference numbers — visa, policy, plate — are mono and tabular so they compare down a column. */
  mono?: boolean
  /**
   * §5.1/§5.2's insured phone. **The only value in this table that is ever a link**: an officer or an
   * expert reading a claim needs to ring that number, and nothing else here is a destination.
   */
  tel?: boolean
}

/**
 * Pass 1's `DetailTable`: two columns on a desktop, label-above-value on a phone (the shell decides
 * which, via `.app-shell--touch`).
 *
 * **Missing is an em dash, never a blank.** Half of these fields are legitimately absent while the
 * NEXT3 cache is cold, and a blank row reads as a rendering fault — which is the wrong thing for an
 * expert to conclude about a claim they are about to photograph.
 *
 * A `<dl>` rather than a `<table>`: these are label/value pairs about one thing, not a grid, and the
 * previous `<table border={1}>` announced a table's worth of navigation to a screen reader for no
 * reason.
 */
export function DetailTable({ rows }: { rows: DetailRow[] }) {
  return (
    <dl className="detail">
      {rows.map((row) => (
        <div key={row.label} style={{ display: 'contents' }}>
          <dt className="detail__label">{row.label}</dt>
          <dd className={`detail__value${row.mono ? ' detail__value--mono' : ''}`}>
            {renderValue(row)}
          </dd>
        </div>
      ))}
    </dl>
  )
}

function renderValue(row: DetailRow): ReactNode {
  if (row.value === null || row.value === undefined || row.value === '') return '—'
  if (row.tel && typeof row.value === 'string') {
    // `tel:` strips the spaces a display format carries; the visible text keeps them.
    return <a href={`tel:${row.value.replace(/\s+/g, '')}`}>{row.value}</a>
  }
  return row.value
}
