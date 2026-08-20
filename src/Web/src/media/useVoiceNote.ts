import { useRef, useState } from 'react'
import {
  RECORDER_EXPLANATIONS,
  RecorderError,
  recordingToFile,
  shortId,
  startBrowserRecording,
  type StartRecording,
  type VoiceRecorder,
} from './recorder'

export interface UseVoiceNoteOptions {
  /** The content types the server accepts for this bucket — the recorder picks the first it can produce. */
  mimeTypes: string[]
  /** Handed the finished recording as a `File`; the caller passes it to `useCapture.select`. */
  onRecorded: (file: File) => void
  start?: StartRecording
  makeId?: () => string
}

export interface UseVoiceNoteResult {
  recording: boolean
  /** True between pressing Stop and the file arriving — the browser flushes its last chunk asynchronously. */
  finishing: boolean
  problem: string | null
  start: () => void
  stop: () => void
}

/**
 * design.md §5.1's voice note, as a hook that knows nothing about assignments or buckets.
 *
 * It is a **sibling of `useCapture`, not a variant of it**. Widening the capture hook to cover audio
 * would put a microphone concern inside the component slice 5.1's garage flow and 5.3/6.1's public
 * page have to reuse unchanged — and §11 holds the calendar at 8 weeks partly on that reuse. So this
 * hook produces a `File` and hands it over; `useCapture.select` takes it from there, routing it to
 * the confirm screen with no verdict because §7.2 item 4 makes the audio gate a playback-confirm.
 */
export function useVoiceNote({
  mimeTypes,
  onRecorded,
  start: startRecording = startBrowserRecording,
  makeId = () => shortId(),
}: UseVoiceNoteOptions): UseVoiceNoteResult {
  const [recording, setRecording] = useState(false)
  const [finishing, setFinishing] = useState(false)
  const [problem, setProblem] = useState<string | null>(null)

  const active = useRef<VoiceRecorder | null>(null)
  // The 1.5 lesson, fourth slice running: two taps in the same tick both read `recording` as false,
  // because React has not re-rendered. A second `getUserMedia` would open a second microphone
  // stream and leave the first one recording with nothing to stop it.
  const busy = useRef(false)

  function start() {
    if (busy.current || active.current) return
    busy.current = true
    setProblem(null)

    startRecording(mimeTypes)
      .then((recorder) => {
        active.current = recorder
        setRecording(true)
      })
      .catch((error: unknown) => {
        setProblem(describe(error))
      })
      .finally(() => {
        busy.current = false
      })
  }

  function stop() {
    const recorder = active.current
    if (!recorder || busy.current) return
    busy.current = true
    active.current = null
    setRecording(false)
    setFinishing(true)

    recorder
      .stop()
      .then((result) => {
        if (result.blob.size === 0) {
          // The server would refuse this as `file_empty`; saying so here saves a round trip and
          // tells the expert the recording did not happen rather than that it was rejected.
          setProblem(RECORDER_EXPLANATIONS.failed)
          return
        }
        onRecorded(recordingToFile(result, makeId()))
      })
      .catch((error: unknown) => {
        setProblem(describe(error))
      })
      .finally(() => {
        busy.current = false
        setFinishing(false)
      })
  }

  return { recording, finishing, problem, start, stop }
}

function describe(error: unknown): string {
  return error instanceof RecorderError
    ? RECORDER_EXPLANATIONS[error.reason]
    : RECORDER_EXPLANATIONS.failed
}
