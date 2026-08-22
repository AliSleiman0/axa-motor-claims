import { Button } from '../ui/Button'
import type { ClarityVerdict } from './clarity'

interface ClarityConfirmProps {
  previewUrl: string
  file: File
  verdict: ClarityVerdict | null
  pending: boolean
  onConfirm: () => void
  onRetake: () => void
}

/**
 * design.md §7.2 item 3 — screen E4: the candidate shown full-bleed with Retake and Confirm.
 *
 * §2 records this as "one shared component; it appears wherever media enters the system", so it
 * takes a URL and two callbacks and knows nothing about buckets, assignments or who is capturing.
 * Nothing is uploaded until Confirm: the point of the screen is that a person looked at the photo.
 */
export function ClarityConfirm({
  previewUrl,
  file,
  verdict,
  pending,
  onConfirm,
  onRetake,
}: ClarityConfirmProps) {
  // A PDF cannot be shown in an <img>. Rendering one anyway produced a broken-image icon on a black
  // bar labelled "the photo about to be sent" — asking the expert to confirm a document they cannot
  // see, and calling it a photo. Naming the file is honest; a broken preview is not. Found in the
  // browser, not by the suite (slice 2.5).
  const isImage = file.type.startsWith('image/')

  // §7.2 item 4: "Voice notes: playback-confirm only". This control *is* the gate for a voice note —
  // there is no signal analysis anywhere in this application (§1), so the expert hearing the
  // recording is the entire check, and a filename would confirm nothing (slice 3.1).
  const isAudio = file.type.startsWith('audio/')

  return (
    <div className="clarity">
      {isImage ? (
        <img className="capture-preview" src={previewUrl} alt="The photo about to be sent to AXA" />
      ) : isAudio ? (
        <div className="stack">
          <p className="clarity__file">
            Listen before sending: <strong>{file.name}</strong>
          </p>
          {/* No caption track: §1 excludes voice transcription from scope entirely. */}
          <audio controls src={previewUrl} />
        </div>
      ) : (
        <p className="clarity__file">
          Ready to send: <strong>{file.name}</strong>
        </p>
      )}

      {verdict ? (
        <p className="caption">
          {verdict.width}×{verdict.height}, sharpness {Math.round(verdict.variance)}
        </p>
      ) : null}

      <div className="actions">
        <Button variant="secondary" onClick={onRetake} disabled={pending}>
          Retake
        </Button>
        <Button variant="primary" onClick={onConfirm} disabled={pending}>
          {pending ? 'Sending…' : 'Confirm'}
        </Button>
      </div>
    </div>
  )
}
