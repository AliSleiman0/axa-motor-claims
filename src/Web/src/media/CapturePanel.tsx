import { useState, type ChangeEvent } from 'react'
import { ClarityConfirm } from './ClarityConfirm'
import type { MediaConfig } from './config'
import { detectNativeShell, NativeCaptureCancelled, type NativeShell } from './nativeShell'
import { useCapture } from './useCapture'
import type { MediaOrigin } from './upload'

interface CapturePanelProps {
  /** The upload endpoint — a prop, so garage (5.1) and the public page (5.3) reuse this component. */
  path: string
  bucket: string
  label: string
  config: MediaConfig | undefined
  count?: number
  /** Passed through to `useCapture` — `'none'` for §5.3's public page (slice 5.3). */
  auth?: 'bearer' | 'none'
  onUploaded?: () => void
  /**
   * What this bucket holds, in the words that finish the control's accessible name — "insured car"
   * gives "Take a photo — insured car" (slice 4.4, pass-1 decision 2).
   *
   * E2 renders **five** of these panels and E3's controls were all called "Take a photo": a screen
   * reader announced five identical buttons, and the expert had to count panels to know which car
   * they were photographing. This is the week-6 accessibility fix, pulled forward because the demo is
   * the first time anyone outside the build sees the screen.
   *
   * The *visible* label stays "Take a photo" / "Choose a file" — pass 1 draws it that way, the demo
   * script names those words, and a visible label that is a prefix of the accessible name is exactly
   * what WCAG 2.5.3 asks for. Optional so a caller with one bucket on a screen need not invent one.
   */
  qualifier?: string
  /**
   * The native shell, when there is one (slice 6.3). A parameter with a default, the house idiom —
   * `decode`'s and `requestPosition`'s shape — so jsdom hands in a fake and production probes.
   *
   * When present, the capture control becomes a **button that calls the system camera** instead of a
   * file input. That is not cosmetic: the Android WebView ignores `capture="environment"` and opens
   * the whole photo library (6.3a), so on that platform the input is a gallery picker wearing a
   * camera's label, and §7.1's capture-only rule is unenforced. Upload inputs are untouched — a
   * picker is *allowed* on `allowUpload` buckets.
   */
  shell?: NativeShell | null
}

/**
 * One §7.1 bucket's capture UI (design.md §5.1's E3).
 *
 * The capture-only rule is expressed by what is rendered: a bucket with `allowUpload: false` gets no
 * file-picker control at all, only a camera input. `capture="environment"` is a hint rather than a
 * guarantee in a browser (§7.1 says so plainly) — the provenance flag on every row records what
 * actually happened, and the Capacitor shell enforces it natively later.
 */
export function CapturePanel({
  path,
  bucket,
  label,
  config,
  count,
  auth,
  onUploaded,
  qualifier,
  shell = detectNativeShell(),
}: CapturePanelProps) {
  const capture = useCapture({ path, bucket, config, auth, onUploaded })

  // `useCapture` owns `problem` and exposes no setter, so a native camera failure needs somewhere of
  // its own to land. Rendered through the same alert paragraph, so there is one place a person
  // looks for what went wrong rather than two.
  const [shellProblem, setShellProblem] = useState<string | null>(null)

  function onPick(origin: MediaOrigin) {
    return (event: ChangeEvent<HTMLInputElement>) => {
      const file = event.target.files?.[0]
      // Clearing the input matters: picking the same file twice in a row is a real thing an expert
      // does after a rejection, and without this the change event never fires the second time.
      event.target.value = ''
      if (file) capture.select(file, origin)
    }
  }

  const busy = capture.stage === 'uploading'
  const named = (action: string) => (qualifier ? `${action} — ${qualifier}` : action)
  const nativeCamera = shell?.hasCamera === true

  function onTakePhoto() {
    setShellProblem(null)
    void shell
      ?.takePhoto(qualifier)
      // Straight into the ordinary pipeline: the clarity gate, the confirm screen and the upload are
      // the same code the browser runs, which is what keeps §7.2 one implementation on both
      // platforms. `captured` is honest here in a way it is not on Android today — the photograph
      // demonstrably came from the camera rather than from a picker claiming to be one.
      .then((file) => capture.select(file, 'captured'))
      .catch((error: unknown) => {
        // Backing out of the camera is an ordinary thing to do and must not read as a failure.
        if (error instanceof NativeCaptureCancelled) return
        setShellProblem('The camera could not be opened. Try again.')
      })
  }

  return (
    <section className="panel">
      <h4 className="panel__title">
        {/* One text node, not a span for the count: the count is part of the heading, and a test
            asserting the shipped string "Insured car photos (3)" reads direct text children only. */}
        {label}
        {typeof count === 'number' ? ` (${count})` : ''}
      </h4>

      {!capture.configLoaded ? (
        <p className="muted">Loading photo settings…</p>
      ) : !capture.ready ? (
        // Not "loading": the settings arrived and this bucket was not among them. Saying so points
        // at the missing §7.1 registry entry and its migration, instead of at the network tab.
        <p className="banner banner--alert" role="alert">
          This section is not configured for uploads yet, so nothing can be sent to it.
        </p>
      ) : capture.candidate && (capture.stage === 'confirm' || capture.stage === 'uploading') ? (
        <ClarityConfirm
          previewUrl={capture.candidate.previewUrl}
          file={capture.candidate.file}
          verdict={capture.candidate.verdict}
          pending={busy}
          onConfirm={capture.confirm}
          onRetake={capture.retake}
        />
      ) : (
        <div className="capture-controls">
          {/*
            The input is visually hidden and the label is the control: clicking the label opens the
            picker exactly as clicking a bare input would, so nothing about the behaviour changed —
            only that it now looks like the button pass 1 draws. The `aria-label` is what makes the
            five panels on E2 distinguishable; the label text stays the shipped string.
          */}
          {nativeCamera ? (
            // No file input at all in the native shell — its presence *is* the gallery route on
            // Android. The visible label and the accessible name are byte-identical to the browser's,
            // so `docs/demo-week4.md`'s script and every `getByLabelText` query read the same on
            // both platforms.
            <button
              type="button"
              className="file-button file-button--primary"
              aria-label={named('Take a photo')}
              onClick={onTakePhoto}
            >
              Take a photo
            </button>
          ) : (
            <>
              <input
                id={`${bucket}-capture`}
                className="file-button__input"
                type="file"
                accept={capture.acceptTypes}
                capture="environment"
                aria-label={named('Take a photo')}
                onChange={onPick('captured')}
              />
              <label className="file-button file-button--primary" htmlFor={`${bucket}-capture`}>
                Take a photo
              </label>
            </>
          )}

          {capture.allowUpload ? (
            <>
              <input
                id={`${bucket}-upload`}
                className="file-button__input"
                type="file"
                accept={capture.acceptTypes}
                aria-label={named('Choose a file')}
                onChange={onPick('uploaded')}
              />
              <label className="file-button" htmlFor={`${bucket}-upload`}>
                Choose a file
              </label>
            </>
          ) : null}

          {capture.stage === 'assessing' ? <p className="muted">Checking the photo…</p> : null}
        </div>
      )}

      {capture.problem || shellProblem ? (
        <p className="banner banner--alert" role="alert">
          {capture.problem ?? shellProblem}
        </p>
      ) : null}
    </section>
  )
}
