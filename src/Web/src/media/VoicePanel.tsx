import { Button } from '../ui/Button'
import { ClarityConfirm } from './ClarityConfirm'
import type { MediaConfig } from './config'
import type { StartRecording } from './recorder'
import { useCapture } from './useCapture'
import { useVoiceNote } from './useVoiceNote'

interface VoicePanelProps {
  /** The upload endpoint — a prop, like `CapturePanel`'s, so this is not expert-specific. */
  path: string
  bucket: string
  label: string
  config: MediaConfig | undefined
  count?: number
  onUploaded?: () => void
  /** Injected by tests; production takes the browser recorder. */
  start?: StartRecording
}

/**
 * design.md §5.1's voice note (§7.2 item 4's playback-confirm).
 *
 * Structurally `CapturePanel` with a Record button where the file inputs are: same render ladder,
 * same confirm screen, same upload path. It is a separate component rather than a mode of
 * `CapturePanel` because a microphone has nothing to do with a camera roll, and the capture
 * component has to stay exactly what slice 5.1 and 5.3/6.1 reuse.
 */
export function VoicePanel({
  path,
  bucket,
  label,
  config,
  count,
  onUploaded,
  start,
}: VoicePanelProps) {
  const capture = useCapture({ path, bucket, config, onUploaded })

  const voice = useVoiceNote({
    // The formats come from the server (`Media.AudioContentTypes`, #10), never from a constant
    // here — the same rule that put §7.2's thresholds behind `GET /api/config/media`.
    mimeTypes: capture.contentTypes,
    onRecorded: (file) => capture.select(file, 'captured'),
    start,
  })

  const busy = capture.stage === 'uploading'

  return (
    <section className="panel">
      <h4 className="panel__title">
        {/* One text node, not a span for the count: the count is part of the heading, and a test
            asserting the shipped string "Insured car photos (3)" reads direct text children only. */}
        {label}
        {typeof count === 'number' ? ` (${count})` : ''}
      </h4>

      {!capture.configLoaded ? (
        <p className="muted">Loading recording settings…</p>
      ) : !capture.ready ? (
        // Not "loading" — 2.5's trap. The settings arrived and this bucket was not among them,
        // which points at a missing §7.1 registry entry and its migration, not at the network.
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
      ) : voice.recording ? (
        <div className="actions">
          <p className="muted" role="status">
            Recording…
          </p>
          <Button variant="primary" onClick={voice.stop}>
            Stop
          </Button>
        </div>
      ) : (
        <div className="actions">
          <Button variant="primary" onClick={voice.start} disabled={voice.finishing}>
            {voice.finishing ? 'Finishing…' : 'Record a voice note'}
          </Button>
        </div>
      )}

      {voice.problem ? (
        <p className="banner banner--alert" role="alert">
          {voice.problem}
        </p>
      ) : null}
      {capture.problem ? (
        <p className="banner banner--alert" role="alert">
          {capture.problem}
        </p>
      ) : null}
    </section>
  )
}
