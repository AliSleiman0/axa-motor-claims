import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { ApiError } from '../api/client'
import { formatDateTime } from '../api/datetime'
import { STATE_LABELS } from '../garage/labels'
import {
  officerDocumentContentPath,
  type DeclarationDocument,
  type OfficerDeclarationDetail,
} from '../officer/api'
import { useDocumentBlobUrl } from '../officer/useDocumentBlobUrl'
import {
  useDecision,
  useOfficerDeclaration,
  useOfficerDocuments,
  useVisaSearch,
} from '../officer/useOfficer'
import type { SvgRenderer } from '../media/png'

/** O2 (design.md §5.2): review the garage's evidence, find the visa, approve or reject. */
export default function OfficerDeclarationPage({ render }: { render?: SvgRenderer } = {}) {
  const { id } = useParams()
  if (!id) return <p role="alert">No declaration selected.</p>
  return <ReviewView declarationId={id} render={render} />
}

function ReviewView({ declarationId, render }: { declarationId: string; render?: SvgRenderer }) {
  const { data, isPending, error } = useOfficerDeclaration(declarationId)
  const [visaNo, setVisaNo] = useState('')
  const [comment, setComment] = useState('')

  if (isPending) return <p>Loading declaration…</p>
  if (error) return <p role="alert">{describe(error)}</p>

  return (
    <section>
      <p>
        <Link to="/officer">← Declarations to review</Link>
      </p>
      <h2>Declaration {data.plateNo}</h2>
      <p>
        Status: <strong>{STATE_LABELS[data.state]}</strong>
      </p>

      <DeclarationFields detail={data} />
      <DocumentList declarationId={declarationId} />

      {data.state === 'submitted' ? (
        <DecisionPanel
          declarationId={declarationId}
          visaNo={visaNo}
          setVisaNo={setVisaNo}
          comment={comment}
          setComment={setComment}
          render={render}
        />
      ) : (
        <DecidedPanel detail={data} />
      )}
    </section>
  )
}

function DeclarationFields({ detail }: { detail: OfficerDeclarationDetail }) {
  return (
    <table border={1} cellPadding={4}>
      <tbody>
        <Row label="Plate" value={detail.plateNo} />
        <Row label="Insured" value={detail.insuredName} />
        <Row label="Garage note" value={detail.note} />
        <Row label="Garage" value={detail.garageName} />
        <Row label="Garage email" value={detail.garageEmail} />
        <Row label="Garage phone" value={detail.garagePhone} />
        <Row label="Submitted" value={detail.submittedAt ? formatDateTime(detail.submittedAt) : null} />
        <Row label="Claim" value={detail.visaNo} />
      </tbody>
    </table>
  )
}

function DocumentList({ declarationId }: { declarationId: string }) {
  const { data, isPending, error } = useOfficerDocuments(declarationId)

  if (isPending) return <p>Loading documents…</p>
  if (error) return <p role="alert">The documents could not be loaded.</p>
  if (!data || data.length === 0) return <p>This declaration has no documents.</p>

  return (
    <section>
      <h3>Documents ({data.length})</h3>
      {data.map((document) => (
        <DocumentRow key={document.id} declarationId={declarationId} document={document} />
      ))}
    </section>
  )
}

/**
 * One document, previewed if it is an image and named if it is not (2.5's lesson: a PDF rendered into
 * an `<img>` is a broken-image icon over alt text calling it a photo, and nothing throws).
 *
 * Neither branch points a `src` or an `href` at the API. A browser sends no Authorization header for
 * an `<img>` or a plain link, so the URL below is always a `blob:` one — see `useDocumentBlobUrl`.
 */
function DocumentRow({
  declarationId,
  document,
}: {
  declarationId: string
  document: DeclarationDocument
}) {
  const isImage = document.contentType.startsWith('image/')
  // Images fetch as soon as the row renders, because they are the preview. Everything else waits for
  // a click — a claim with a dozen photographs and three PDFs should not pull the PDFs too.
  const [wanted, setWanted] = useState(false)
  const preview = useDocumentBlobUrl(officerDocumentContentPath(declarationId, document.id), {
    enabled: document.blobRetained && (isImage || wanted),
  })

  const name = document.fileName ?? document.bucket

  return (
    <section>
      <h4>
        {name} <em>({document.bucket})</em>
      </h4>

      {!document.blobRetained ? (
        // §7.3 swept the bytes once NEXT3 confirmed the push, and that is terminal. Saying so beats
        // a link that 404s, and beats a broken image far more.
        <p>Sent to AXA; the local copy has been removed.</p>
      ) : isImage ? (
        preview.url ? (
          <img className="capture-preview" src={preview.url} alt={`Document ${name}`} />
        ) : preview.failed ? (
          <p role="alert">This document could not be loaded.</p>
        ) : (
          <p>Loading preview…</p>
        )
      ) : preview.url ? (
        <p>
          <a href={preview.url} download={name} target="_blank" rel="noreferrer">
            Open {name}
          </a>
        </p>
      ) : (
        <p>
          <button
            type="button"
            disabled={preview.pending}
            onClick={() => {
              setWanted(true)
            }}
          >
            {preview.pending ? 'Opening…' : `Open ${name}`}
          </button>
          {preview.failed && <span role="alert"> This document could not be loaded.</span>}
        </p>
      )}
    </section>
  )
}

function DecisionPanel({
  declarationId,
  visaNo,
  setVisaNo,
  comment,
  setComment,
  render,
}: {
  declarationId: string
  visaNo: string
  setVisaNo: (next: string) => void
  comment: string
  setComment: (next: string) => void
  render?: SvgRenderer
}) {
  const decision = useDecision(declarationId, { render })

  return (
    <section>
      <VisaSearch onUse={setVisaNo} />

      <h3>Decision</h3>
      <p>
        <label htmlFor="visa-no">Visa number</label>{' '}
        <input
          id="visa-no"
          value={visaNo}
          onChange={(event) => {
            setVisaNo(event.target.value)
          }}
        />
      </p>
      <p>
        <label htmlFor="decision-comment">Comments</label>{' '}
        <textarea
          id="decision-comment"
          value={comment}
          maxLength={2000}
          onChange={(event) => {
            setComment(event.target.value)
          }}
        />
      </p>
      {/*
        One latch across both buttons: they are alternatives, and a screen that let both be pressed
        at once would be racing its own user. Approve renders and uploads #18's decision image before
        it calls approve, so a render or upload failure leaves the declaration untouched.
      */}
      <button
        type="button"
        disabled={decision.pending}
        onClick={() => {
          decision.approve(visaNo, comment)
        }}
      >
        {decision.pending ? 'Working…' : 'Approve'}
      </button>{' '}
      <button
        type="button"
        disabled={decision.pending}
        onClick={() => {
          decision.reject(comment)
        }}
      >
        Reject
      </button>
      <p>
        Approving sends the garage&rsquo;s documents and a record of this decision to AXA under the
        visa above. Rejecting is final — the garage would have to file a new declaration.
      </p>
      {decision.failed && <p role="alert">{decision.failed}</p>}
    </section>
  )
}

function VisaSearch({ onUse }: { onUse: (visaNo: string) => void }) {
  const [plateNo, setPlateNo] = useState('')
  const [visaNo, setVisaNo] = useState('')
  const search = useVisaSearch()

  return (
    <section>
      <h3>Find the claim in NEXT3</h3>
      <p>
        <label htmlFor="search-plate">Plate</label>{' '}
        <input
          id="search-plate"
          value={plateNo}
          onChange={(event) => {
            setPlateNo(event.target.value)
          }}
        />{' '}
        <label htmlFor="search-visa">Visa</label>{' '}
        <input
          id="search-visa"
          value={visaNo}
          onChange={(event) => {
            setVisaNo(event.target.value)
          }}
        />{' '}
        <button
          type="button"
          disabled={search.pending}
          onClick={() => {
            search.run(plateNo, visaNo)
          }}
        >
          {search.pending ? 'Searching…' : 'Search'}
        </button>
      </p>

      {search.failed && <p role="alert">{search.failed}</p>}

      {search.results !== null &&
        (search.results.length === 0 ? (
          // #16, verbatim from §5.2's officer-review row: there is no create-visa API and this
          // release does not build one, so the officer leaves, creates it, and searches again.
          <p role="alert">
            No claim matches. Create the visa in NEXT3, then search again.
          </p>
        ) : (
          <table border={1} cellPadding={4}>
            <thead>
              <tr>
                <th>Visa</th>
                <th>Plate</th>
                <th>Insured</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {search.results.map((result) => (
                <tr key={result.visaNo}>
                  <td>{result.visaNo}</td>
                  <td>{result.plateNo}</td>
                  <td>{result.insuredName}</td>
                  <td>
                    <button
                      type="button"
                      onClick={() => {
                        onUse(result.visaNo)
                      }}
                    >
                      Use this visa
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        ))}
    </section>
  )
}

function DecidedPanel({ detail }: { detail: OfficerDeclarationDetail }) {
  return (
    <section>
      <h3>Decision</h3>
      <p>
        This declaration was {STATE_LABELS[detail.state].toLowerCase()}
        {detail.decidedAt ? ` on ${formatDateTime(detail.decidedAt)}` : ''}.
      </p>
      {/* The officer sees every comment in every state — §5.2's visibility rule constrains the
          garage's view only, and a second officer reading this needs to know what the first said. */}
      {detail.comments.length > 0 && (
        <ul>
          {detail.comments.map((comment) => (
            <li key={`${comment.createdAt}-${comment.body}`}>
              {comment.body} <em>({formatDateTime(comment.createdAt)})</em>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function Row({ label, value }: { label: string; value?: string | null }) {
  return (
    <tr>
      <th align="left">{label}</th>
      <td>{value ?? '—'}</td>
    </tr>
  )
}

function describe(error: Error): string {
  if (error instanceof ApiError && error.status === 404) {
    return 'That declaration no longer exists.'
  }

  return `Could not load the declaration${error instanceof ApiError ? ` (${error.status})` : ''}.`
}
