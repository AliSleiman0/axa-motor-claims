import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import { ApiError } from '../api/client'
import { CapturePanel } from '../media/CapturePanel'
import { CarSideSelector } from '../media/carshots/CarSideSelector'
import { useMediaConfig } from '../media/useMediaConfig'
import { AlertBanner, StatusBanner } from '../ui/Banner'
import { Button } from '../ui/Button'
import { DateField, MoneyField, SelectField, TextField } from '../ui/fields'
import {
  PUBLIC_DOCUMENT_BUCKET,
  describeSubmitError,
  fetchPublicDocuments,
  fetchPublicLink,
  publicDocumentsPath,
  publicKeys,
  submitPublicRequest,
  type PublicDocument,
  type PublicLinkView,
} from './api'
import { CAR_SHOT_PANELS, carShotPanel } from './carShots'

type Stage = 'details' | 'documents'

/**
 * What left, captured at the moment of sending. Two counts rather than one because §5.3 asks for two
 * different things and the customer did two different jobs; null means "not sent yet" and is what the
 * success stage is derived from, so there is one source of truth rather than a flag and a number that
 * can disagree.
 */
interface Sent {
  documents: number
  photographs: number
}

interface Draft {
  insuredName: string
  insuranceType: string
  insuredAddress: string
  carValue: string
  estimatedPremium: string
  effectiveDate: string
}

const EMPTY: Draft = {
  insuredName: '',
  insuranceType: '',
  insuredAddress: '',
  carValue: '',
  estimatedPremium: '',
  effectiveDate: '',
}

/**
 * P1 (design.md §5.3, §9.1) — the Option 2 public form.
 *
 * **The only screen in this application with no account behind it.** No shell, no header navigation,
 * no sign-out, no push panel, and no token in `localStorage`: one link, one visit, one submission.
 * The chrome is hand-built from the same CSS classes `AppShell` uses, exactly as `InvitePage` does,
 * because `AppHeader` reads the session and this page has none.
 *
 * **Nothing typed here is kept between visits.** The API has one write — the submit — so the six
 * fields live in component state and a customer who closes the tab returns to an empty form with
 * their uploaded documents still listed. Deliberate: the alternative is a member of the public's
 * name and address sitting in browser storage on what may be a shared phone, which is the opposite
 * of what §9.1 is for. Recorded in `scope-decisions.md`.
 */
export default function PublicRequestPage() {
  const { token = '' } = useParams()

  const link = useQuery({
    queryKey: publicKeys.link(token),
    queryFn: ({ signal }) => fetchPublicLink(token, signal),
    // A dead link is a permanent answer, not a blip: §9.1 gives invalid, expired and already-used
    // tokens one identical 404, so retrying spends a customer's connection to be told the same thing.
    retry: false,
    enabled: token.length > 0,
  })

  // A 429 is not a dead link, and saying it is would be the worst lie this page can tell: §9.1's
  // per-token limiter counts every call a busy customer makes, so somebody photographing their car
  // quickly can trip it while holding a perfectly valid link. Told "ask your broker for a new one",
  // they would — and the broker would issue one, and the old one would be dead for real.
  if (link.error instanceof ApiError && link.error.status === 429) return <TooFast />
  if (token.length === 0 || link.isError) return <Expired />
  if (link.isPending || !link.data) return <Loading />

  return <Form token={token} view={link.data} />
}

/**
 * The page's own chrome, hand-built from the same CSS `AppShell` uses — `AppHeader` reads the
 * session and this page has none, which is `InvitePage`'s reason for doing the same thing.
 *
 * **`app-shell--touch` is not decoration.** That class is what sets `--control: 48px`; without it
 * every control on the page inherits the 36 px desktop size, and a browser pass measured exactly that
 * here. This is the most phone-only screen in the product — a member of the public, on their own
 * handset, following a link from their broker — so it is the last screen that should have been
 * getting a desk-sized touch target. `shellFor` decides the class by *role* and there is no role
 * here, which is precisely how it was missed.
 */
function Chrome({ children }: { children: React.ReactNode }) {
  return (
    <div className="app-shell app-shell--touch">
      <header className="app-header">
        <span className="app-header__brand">AXA Motor Claims</span>
      </header>
      <main className="app-main app-main--narrow">{children}</main>
    </div>
  )
}

function Loading() {
  return (
    <Chrome>
      <section className="page">
        <StatusBanner>Opening your form…</StatusBanner>
      </section>
    </Chrome>
  )
}

/**
 * P1Expired — one screen for five different causes: never valid, mistyped, expired, already
 * submitted, and the loser of two simultaneous submissions. The server answers all five identically
 * and so does this: **no broker name, no dates, no retry, no support number.** A link that has closed
 * must not confirm that it ever existed or who it belonged to.
 */
function Expired() {
  return (
    <Chrome>
      <section className="page">
        <h1 className="page__title">This link is no longer open</h1>
        <p className="muted">
          It may already have been used, or it may have run out. Ask your broker to send you a new
          one.
        </p>
      </section>
    </Chrome>
  )
}

/**
 * §9.1's rate limiter, seen from the customer's side. Deliberately says nothing about the link — it
 * reveals no more than the uniform 404 does — but it says the one thing that is different and
 * actionable: wait, then reload. The link is still theirs.
 */
function TooFast() {
  return (
    <Chrome>
      <section className="page">
        <h1 className="page__title">One moment</h1>
        <p className="muted">
          That was a lot of requests at once. Wait a minute and reload this page — your link is still
          open and nothing you have sent is lost.
        </p>
      </section>
    </Chrome>
  )
}

function Form({ token, view }: { token: string; view: PublicLinkView }) {
  const [stage, setStage] = useState<Stage>('details')
  const [draft, setDraft] = useState<Draft>(EMPTY)
  const [problem, setProblem] = useState<string | null>(null)
  /**
   * How many documents went, **captured at the moment of sending**.
   *
   * P1Success counts what was sent, and that count cannot be read from the documents query after the
   * fact: the submit locks the token, so the next fetch of `/public/{token}/documents` is §9.1's
   * uniform 404 and the list is empty for ever. A browser pass caught this reporting **"0 documents"**
   * to a customer who had just watched two upload — on the one screen whose entire job is to say what
   * left, to somebody with no account, no receipt and no way to check.
   *
   * Null means "not sent yet" and is what the stage is derived from, so there is one source of truth
   * for both rather than a flag and a number that can disagree.
   */
  const [sent, setSent] = useState<Sent | null>(null)
  /**
   * Which side the one capture panel is pointed at. It starts on the front and **never moves on its
   * own**: a shot fills its side in green and leaves the choice where the customer put it. Advancing
   * automatically would move the control under a thumb that is still on it, and the done-fill is
   * already the feedback (smaller interpretation, recorded in `scope-decisions.md`).
   */
  const [side, setSide] = useState<string>(CAR_SHOT_PANELS[0].id)
  const client = useQueryClient()
  const config = useMediaConfig()

  const documents = useQuery({
    queryKey: publicKeys.documents(token),
    queryFn: ({ signal }) => fetchPublicDocuments(token, signal),
    retry: false,
    // Stopped once the link is spent: refetching it would ask a dead token a question whose only
    // answer is the uniform 404.
    enabled: sent === null,
  })

  // One query, two lists. `GET /public/{token}/documents` carries the supporting documents **and**
  // the car shots since slice 6.1, each with its bucket, so the page splits them here rather than the
  // server answering two endpoints — and the "N of 5" and the done marks are read off the same list
  // the server would refuse the submit against.
  const all: PublicDocument[] = documents.data ?? []
  const attached = all.filter((document) => document.bucket === PUBLIC_DOCUMENT_BUCKET)
  const shotBuckets = new Set(all.map((document) => document.bucket))
  const sides = CAR_SHOT_PANELS.map((panel) => ({
    id: panel.id,
    label: panel.label,
    done: shotBuckets.has(panel.bucket),
  }))
  const shotsTaken = sides.filter((entry) => entry.done).length
  const allShotsTaken = shotsTaken === CAR_SHOT_PANELS.length
  const selected = carShotPanel(side)

  // A `useRef` latch rather than `isPending`: two taps in the same tick both run before React
  // re-renders, so state has not caught up and the second would send a second submission. 1.5's
  // lesson, sixth outing — and here the loser would be answered with the uniform 404, which on this
  // screen reads as "your form was lost".
  const inFlight = useRef(false)

  const submit = useMutation({
    mutationFn: () =>
      submitPublicRequest(token, {
        insuredName: draft.insuredName.trim(),
        insuranceType: draft.insuranceType,
        insuredAddress: draft.insuredAddress.trim(),
        carValue: Number(draft.carValue),
        estimatedPremium: Number(draft.estimatedPremium),
        effectiveDate: draft.effectiveDate,
      }),
    onSuccess: () => setSent({ documents: attached.length, photographs: shotsTaken }),
    onError: (error: unknown) => setProblem(describeSubmitError(error)),
    onSettled: () => {
      inFlight.current = false
    },
  })

  function set<K extends keyof Draft>(key: K) {
    return (value: string) => setDraft((current) => ({ ...current, [key]: value }))
  }

  const detailsComplete =
    draft.insuredName.trim().length > 0 &&
    draft.insuranceType.length > 0 &&
    draft.insuredAddress.trim().length > 0 &&
    Number(draft.carValue) > 0 &&
    Number(draft.estimatedPremium) > 0 &&
    draft.effectiveDate.length > 0

  if (sent !== null) {
    return <Success view={view} sent={sent} />
  }

  function send() {
    if (inFlight.current) return
    inFlight.current = true
    setProblem(null)
    submit.mutate()
  }

  return (
    <Chrome>
      <section className="page">
        <h1 className="page__title">
          {stage === 'details' ? 'Your motor insurance details' : 'Documents and photographs'}
        </h1>

        {stage === 'details' ? (
          <>
            <p className="muted">
              {view.brokerDisplayName
                ? `${view.brokerDisplayName} asked you to complete this.`
                : 'You have been asked to complete this.'}{' '}
              It takes a few minutes: six details and your documents.
            </p>
            <p className="muted">
              You can close this page and come back to the same link. It stops working once you send
              it — and anything you have typed but not sent is not kept, so finish in one go if you
              can.
            </p>

            <div className="panel">
              <TextField
                id="insuredName"
                label="Insured name"
                value={draft.insuredName}
                onChange={(e) => set('insuredName')(e.target.value)}
              />
              <SelectField
                id="insuranceType"
                label="Insurance type"
                options={view.insuranceTypes}
                placeholder="Choose one"
                value={draft.insuranceType}
                onChange={(e) => set('insuranceType')(e.target.value)}
              />
              <TextField
                id="insuredAddress"
                label="Address"
                value={draft.insuredAddress}
                onChange={(e) => set('insuredAddress')(e.target.value)}
              />
              <MoneyField
                id="carValue"
                label="Car value"
                value={draft.carValue}
                onChange={(e) => set('carValue')(e.target.value)}
              />
              <MoneyField
                id="estimatedPremium"
                label="Estimated premium"
                hint="Both amounts must be more than zero."
                value={draft.estimatedPremium}
                onChange={(e) => set('estimatedPremium')(e.target.value)}
              />
              <DateField
                id="effectiveDate"
                label="Effective date"
                value={draft.effectiveDate}
                onChange={(e) => set('effectiveDate')(e.target.value)}
              />

              <Button
                variant="primary"
                block
                disabled={!detailsComplete}
                onClick={() => setStage('documents')}
              >
                Continue to documents
              </Button>
              {!detailsComplete && (
                <p className="muted">
                  Every detail above is needed before this can be sent. Nothing you have typed is
                  lost — fill in the empty ones and continue.
                </p>
              )}
            </div>
          </>
        ) : (
          <>
            <p className="muted">
              Up to {view.maxFiles} files, each under {view.maxFileMb} MB.
            </p>

            <CapturePanel
              path={publicDocumentsPath(token)}
              bucket={PUBLIC_DOCUMENT_BUCKET}
              label={`Supporting documents (${attached.length})`}
              qualifier="a supporting document"
              config={config.data}
              // No session, and none is wanted: see `UploadRequest.auth`.
              auth="none"
              onUploaded={() => {
                void client.invalidateQueries({ queryKey: publicKeys.documents(token) })
              }}
            />

            {attached.length > 0 && (
              <div className="panel">
                <h2 className="panel__title">Attached</h2>
                {attached.map((document) => (
                  <div key={document.id} className="doc-row">
                    <span className="doc-row__name">{document.fileName ?? 'Attached file'}</span>
                  </div>
                ))}
              </div>
            )}

            {/*
              §5.3's five mandatory car sides (slice 6.1). Every one is required before the server
              will accept the submission, and each goes through the same clarity gate as an expert's
              photograph — refused on this phone, before anything is sent.
            */}
            <div className="panel">
              <h2 className="panel__title">Photographs of the car ({shotsTaken} of 5)</h2>
              <p className="muted">
                Tap a part of the car, then take that photograph. All five are needed.
              </p>

              <CarSideSelector sides={sides} selected={side} onSelect={setSide} />

              {/*
                One panel, pointed at whichever side is selected — five buckets, so its input id stays
                unique and the file it sends is filed under the side it was taken for.

                `key` is load-bearing: it remounts the panel when the side changes, so a photograph
                sitting on the clarity confirm screen is discarded rather than confirmed into the
                bucket the customer has just switched to.
              */}
              <CapturePanel
                key={selected.bucket}
                path={publicDocumentsPath(token)}
                bucket={selected.bucket}
                label={`${selected.label} of the car`}
                qualifier={selected.qualifier}
                config={config.data}
                auth="none"
                onUploaded={() => {
                  void client.invalidateQueries({ queryKey: publicKeys.documents(token) })
                }}
              />
            </div>

            {problem && <AlertBanner>{problem}</AlertBanner>}

            <Button
              variant="primary"
              block
              disabled={
                !detailsComplete || attached.length === 0 || !allShotsTaken || submit.isPending
              }
              onClick={send}
            >
              {submit.isPending ? 'Sending…' : 'Send to AXA'}
            </Button>
            {attached.length === 0 && (
              <p className="muted">
                Attach at least one supporting document — your identity card or the car papers.
              </p>
            )}
            {!allShotsTaken && (
              <p className="muted">
                All five photographs of the car are needed. {shotsTaken} of 5 taken.
              </p>
            )}

            <Button block onClick={() => setStage('details')}>
              Back to your details
            </Button>
          </>
        )}
      </section>
    </Chrome>
  )
}

/**
 * P1Success. **The summary counts what was sent rather than repeating it**: the page is public and
 * somebody may hand the phone back at this point. No account was created and there is nowhere to
 * sign in — the customer's relationship is with their broker, not with this app.
 *
 * **The documents and the photographs are counted separately** (slice 6.1). They are separate efforts
 * — a scan of the car papers and five walks around the vehicle — and folding them into one number on
 * the one screen whose whole job is to say what left would be the same mistake, one size smaller,
 * that the browser pass caught here reporting "0 documents".
 */
function Success({ view, sent }: { view: PublicLinkView; sent: Sent }) {
  return (
    <Chrome>
      <section className="page">
        <h1 className="page__title">Sent to your broker</h1>
        <p className="muted">
          {view.brokerDisplayName ? `${view.brokerDisplayName} has` : 'Your broker has'} your six
          details, {sent.documents === 1 ? '1 document' : `${sent.documents} documents`} and{' '}
          {sent.photographs === 1 ? '1 photograph' : `${sent.photographs} photographs`} of the car.
          They will be in touch about the quotation.
        </p>
        <StatusBanner>
          This link has now closed. If something was wrong, ask your broker for a new one — it cannot
          be reopened.
        </StatusBanner>
      </section>
    </Chrome>
  )
}
