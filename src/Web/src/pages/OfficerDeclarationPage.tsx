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
  type DecisionStep,
} from '../officer/useOfficer'
import type { SvgRenderer } from '../media/png'
import { AlertBanner, StatusBanner } from '../ui/Banner'
import { Button } from '../ui/Button'
import { DetailTable } from '../ui/DetailTable'
import { DocumentRow, PushIndicator } from '../ui/DocumentRow'
import { StatusChip } from '../ui/StatusChip'
import { TextArea, TextField } from '../ui/fields'
import { Worklist } from '../ui/Worklist'

/** O2 (design.md §5.2): review the garage's evidence, find the visa, approve or reject. */
export default function OfficerDeclarationPage({ render }: { render?: SvgRenderer } = {}) {
  const { id } = useParams()
  if (!id) return <AlertBanner>No declaration selected.</AlertBanner>
  return <ReviewView declarationId={id} render={render} />
}

function ReviewView({ declarationId, render }: { declarationId: string; render?: SvgRenderer }) {
  const { data, isPending, error } = useOfficerDeclaration(declarationId)
  const [visaNo, setVisaNo] = useState('')
  const [comment, setComment] = useState('')

  if (isPending) return <p className="muted">Loading declaration…</p>
  if (error) return <AlertBanner>{describe(error)}</AlertBanner>

  return (
    <section className="page">
      <Link className="back-link" to="/officer">
        ← Declarations to review
      </Link>
      <div className="page__head">
        <h2 className="page__title">Declaration {data.plateNo}</h2>
        <StatusChip label={STATE_LABELS[data.state]} />
      </div>
      <p className="muted">
        Status: <strong>{STATE_LABELS[data.state]}</strong>
      </p>

      {/*
        Three columns on a desktop, one on anything narrower (pass 3's O2States). **The left and
        centre columns are identical in every state** — an officer can always read what the garage
        sent, whether the declaration is waiting, decided or unreachable — so only the right column
        branches. That is the layout carrying a rule rather than decorating one.
      */}
      <div className="review">
        <div className="review__col">
          <DeclarationFields detail={data} />
        </div>
        <div className="review__col">
          <DocumentList declarationId={declarationId} />
        </div>
        <div className="review__col">
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
        </div>
      </div>
    </section>
  )
}

function DeclarationFields({ detail }: { detail: OfficerDeclarationDetail }) {
  return (
    <DetailTable
      rows={[
        { label: 'Plate', value: detail.plateNo, mono: true },
        { label: 'Insured', value: detail.insuredName },
        { label: 'Garage note', value: detail.note },
        { label: 'Garage', value: detail.garageName },
        { label: 'Garage email', value: detail.garageEmail },
        { label: 'Garage phone', value: detail.garagePhone, mono: true, tel: true },
        {
          label: 'Submitted',
          value: detail.submittedAt ? formatDateTime(detail.submittedAt) : null,
        },
        { label: 'Claim', value: detail.visaNo, mono: true },
      ]}
    />
  )
}

function DocumentList({ declarationId }: { declarationId: string }) {
  const { data, isPending, error } = useOfficerDocuments(declarationId)

  if (isPending) return <p className="muted">Loading documents…</p>
  if (error) return <AlertBanner>The documents could not be loaded.</AlertBanner>
  if (!data || data.length === 0) return <p className="muted">This declaration has no documents.</p>

  return (
    <section className="panel">
      <h3 className="panel__title">Documents ({data.length})</h3>
      {data.map((document) => (
        <ReviewDocument key={document.id} declarationId={declarationId} document={document} />
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
function ReviewDocument({
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
    <DocumentRow
      name={name}
      bucket={document.bucket}
      /*
        Where this document is in the §6.3 queue, in pass 1's three words. Grey text and never a
        chip: it is background to the officer's decision, not a status to act on. `deferred` renders
        nothing at all, which is right — before approval there is no push to be in a state about.
      */
      indicator={
        <PushIndicator
          pushStatus={document.pushStatus}
          pushConfirmed={document.pushConfirmed}
          blobRetained={document.blobRetained}
        />
      }
    >
      {!document.blobRetained ? (
        // Nothing in the body: §7.3 swept the bytes once NEXT3 confirmed the push, and that is
        // terminal — there is no preview to show and no link to offer. **The indicator above says
        // so**, and it says it once. It used to be a second `<p>` here with the same sentence, which
        // was fine while nothing else rendered it and became a duplicate the moment `PushIndicator`
        // was wired in — the shape of bug this codebase keeps calling "two answers that can
        // disagree", in its mildest form.
        null
      ) : isImage ? (
        preview.url ? (
          <img className="capture-preview" src={preview.url} alt={`Document ${name}`} />
        ) : preview.failed ? (
          <p className="banner banner--alert" role="alert">
            This document could not be loaded.
          </p>
        ) : (
          <p className="muted">Loading preview…</p>
        )
      ) : preview.url ? (
        <p>
          <a href={preview.url} download={name} target="_blank" rel="noreferrer">
            Open {name}
          </a>
        </p>
      ) : (
        <p className="actions">
          <Button
            variant="secondary"
            disabled={preview.pending}
            onClick={() => {
              setWanted(true)
            }}
          >
            {preview.pending ? 'Opening…' : `Open ${name}`}
          </Button>
          {preview.failed && <span role="alert"> This document could not be loaded.</span>}
        </p>
      )}
    </DocumentRow>
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
    <section className="stack">
      <VisaSearch onUse={setVisaNo} />

      <div className="panel">
        <h3 className="panel__title">Decision</h3>
        <TextField
          id="visa-no"
          label="Visa number"
          value={visaNo}
          className="field__control--mono"
          onChange={(event) => {
            setVisaNo(event.target.value)
          }}
        />
        <TextArea
          id="decision-comment"
          label="Comments"
          value={comment}
          maxLength={2000}
          onChange={(event) => {
            setComment(event.target.value)
          }}
        />
        {/*
          One latch across both buttons: they are alternatives, and a screen that let both be pressed
          at once would be racing its own user. Approve renders and uploads #18's decision image
          before it calls approve, so a render or upload failure leaves the declaration untouched.

          Reject is outlined and never filled — two filled buttons of equal weight would be a screen
          with no opinion about which outcome is the ordinary one.
        */}
        <div className="actions">
          <Button
            variant="primary"
            disabled={decision.pending}
            onClick={() => {
              decision.approve(visaNo, comment)
            }}
          >
            {decision.pending ? approveLabel(decision.step) : 'Approve'}
          </Button>
          <Button
            variant="destructive"
            disabled={decision.pending}
            onClick={() => {
              decision.reject(comment)
            }}
          >
            Reject
          </Button>
        </div>
        <p className="muted">
          Approving sends the garage&rsquo;s documents and a record of this decision to AXA under the
          visa above. Rejecting is final — the garage would have to file a new declaration.
        </p>
        {decision.failed && <AlertBanner>{decision.failed}</AlertBanner>}
      </div>
    </section>
  )
}

/**
 * Approve's label while it runs. The three steps are real and they are not equivalent — a failure at
 * *Rendering* or *Uploading* leaves the declaration undecided, and only the third commits — so the
 * officer is told which one they are on rather than watching one flat word for all three.
 *
 * `Working…` is the fallback and is what a **rejection** shows throughout, because a rejection is a
 * single call with nothing to narrate.
 */
function approveLabel(step: DecisionStep | null): string {
  if (step === 'rendering') return 'Rendering decision…'
  if (step === 'uploading') return 'Uploading…'
  if (step === 'approving') return 'Approving…'
  return 'Working…'
}

function VisaSearch({ onUse }: { onUse: (visaNo: string) => void }) {
  const [plateNo, setPlateNo] = useState('')
  const [visaNo, setVisaNo] = useState('')
  const search = useVisaSearch()

  return (
    <section className="panel">
      <h3 className="panel__title">Find the claim in NEXT3</h3>
      <div className="field-row">
        <TextField
          id="search-plate"
          label="Plate"
          value={plateNo}
          className="field__control--mono"
          onChange={(event) => {
            setPlateNo(event.target.value)
          }}
        />
        <TextField
          id="search-visa"
          label="Visa"
          value={visaNo}
          className="field__control--mono"
          onChange={(event) => {
            setVisaNo(event.target.value)
          }}
        />
        <Button
          variant="primary"
          disabled={search.pending}
          onClick={() => {
            search.run(plateNo, visaNo)
          }}
        >
          {search.pending ? 'Searching…' : 'Search'}
        </Button>
      </div>

      {search.failed && <AlertBanner>{search.failed}</AlertBanner>}

      {search.results !== null &&
        (search.results.length === 0 ? (
          // #16, verbatim from §5.2's officer-review row: there is no create-visa API and this
          // release does not build one, so the officer leaves, creates it, and searches again.
          <AlertBanner>No claim matches. Create the visa in NEXT3, then search again.</AlertBanner>
        ) : (
          <Worklist headers={['Visa', 'Plate', 'Insured', '']}>
            {search.results.map((result) => (
              <tr key={result.visaNo}>
                <td className="mono">{result.visaNo}</td>
                <td className="mono">{result.plateNo}</td>
                <td>{result.insuredName}</td>
                <td>
                  <Button
                    variant="secondary"
                    onClick={() => {
                      onUse(result.visaNo)
                    }}
                  >
                    Use this visa
                  </Button>
                </td>
              </tr>
            ))}
          </Worklist>
        ))}
    </section>
  )
}

function DecidedPanel({ detail }: { detail: OfficerDeclarationDetail }) {
  return (
    <section className="panel">
      <h3 className="panel__title">Decision</h3>
      <StatusBanner>
        This declaration was {STATE_LABELS[detail.state].toLowerCase()}
        {detail.decidedAt ? ` on ${formatDateTime(detail.decidedAt)}` : ''}.
      </StatusBanner>
      {/* The officer sees every comment in every state — §5.2's visibility rule constrains the
          garage's view only, and a second officer reading this needs to know what the first said. */}
      {detail.comments.length > 0 && (
        <ul className="stack">
          {detail.comments.map((comment) => (
            <li key={`${comment.createdAt}-${comment.body}`}>
              {comment.body} <em className="caption">({formatDateTime(comment.createdAt)})</em>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function describe(error: Error): string {
  if (error instanceof ApiError && error.status === 404) {
    return 'That declaration no longer exists.'
  }

  return `Could not load the declaration${error instanceof ApiError ? ` (${error.status})` : ''}.`
}
