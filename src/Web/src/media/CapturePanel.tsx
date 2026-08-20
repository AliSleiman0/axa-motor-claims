import type { ChangeEvent } from 'react'
import { ClarityConfirm } from './ClarityConfirm'
import type { MediaConfig } from './config'
import { useCapture } from './useCapture'
import type { MediaOrigin } from './upload'

interface CapturePanelProps {
  /** The upload endpoint — a prop, so garage (5.1) and the public page (5.3) reuse this component. */
  path: string
  bucket: string
  label: string
  config: MediaConfig | undefined
  count?: number
  onUploaded?: () => void
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
  onUploaded,
}: CapturePanelProps) {
  const capture = useCapture({ path, bucket, config, onUploaded })

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

  return (
    <section>
      <h4>
        {label}
        {typeof count === 'number' ? ` (${count})` : ''}
      </h4>

      {!capture.configLoaded ? (
        <p>Loading photo settings…</p>
      ) : !capture.ready ? (
        // Not "loading": the settings arrived and this bucket was not among them. Saying so points
        // at the missing §7.1 registry entry and its migration, instead of at the network tab.
        <p role="alert">
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
        <div>
          <label htmlFor={`${bucket}-capture`}>Take a photo</label>{' '}
          <input
            id={`${bucket}-capture`}
            type="file"
            accept={capture.acceptTypes}
            capture="environment"
            onChange={onPick('captured')}
          />

          {capture.allowUpload ? (
            <div>
              <label htmlFor={`${bucket}-upload`}>Choose a file</label>{' '}
              <input
                id={`${bucket}-upload`}
                type="file"
                accept={capture.acceptTypes}
                onChange={onPick('uploaded')}
              />
            </div>
          ) : null}

          {capture.stage === 'assessing' ? <p>Checking the photo…</p> : null}
        </div>
      )}

      {capture.problem ? <p role="alert">{capture.problem}</p> : null}
    </section>
  )
}
