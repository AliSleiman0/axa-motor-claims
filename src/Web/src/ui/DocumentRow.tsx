import type { ReactNode } from 'react'

/** §4's `document.push_status`, in the words pass 1 fixes for the three states. */
export function PushIndicator({
  pushStatus,
  blobRetained,
}: {
  pushStatus?: string | null
  blobRetained?: boolean
}) {
  // §7.3's sweep removed the bytes once NEXT3 confirmed the push, and that is terminal. Saying so
  // beats a link that 404s, and beats a broken image far more.
  if (blobRetained === false) {
    return <span className="push-indicator">Sent to AXA; the local copy has been removed</span>
  }
  if (pushStatus === 'queued') return <span className="push-indicator">Queued, will send</span>
  if (pushStatus === 'sent') return <span className="push-indicator">Sent to AXA</span>
  return null
}

export interface DocumentRowProps {
  name: string
  bucket: string
  indicator?: ReactNode
  children?: ReactNode
}

/**
 * One document in a list (pass 1's `DocumentRow`): the file name, the bucket it went into, the push
 * indicator, and whatever preview or control the caller supplies underneath.
 *
 * The bucket is shown raw. An officer reading a row to a developer needs the real string — the same
 * argument pass 3 makes for A2's operation names.
 */
export function DocumentRow({ name, bucket, indicator, children }: DocumentRowProps) {
  return (
    // A `<section>` with a heading, not a bare div: each row is a labelled region, which is how an
    // officer scanning a claim's evidence with a screen reader gets from one document to the next.
    <section className="doc-row">
      <div className="doc-row__head">
        <h4 className="doc-row__name">{name}</h4>
        <span className="doc-row__bucket mono">{bucket}</span>
      </div>
      {indicator}
      {children}
    </section>
  )
}
