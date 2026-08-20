/**
 * design.md §5.1's voice note: record → playback confirm. Nothing more (§1 excludes transcription).
 *
 * The whole browser surface sits behind one injectable function, the `requestPosition` /
 * `decodeImage` idiom taken one level further: jsdom has neither `MediaRecorder` nor
 * `navigator.mediaDevices`, so a test that stubbed them would be testing its own stubs. Injecting
 * the *start* of a recording instead leaves a seam a fake can fill honestly, and leaves everything
 * below it — permissions, codec negotiation, track teardown — to the manual browser pass, which is
 * the only place it can be proven anyway.
 */

/** What a finished recording yields: the bytes, and the container the browser actually used. */
export interface VoiceRecording {
  blob: Blob
  /** As the recorder reports it — `audio/webm;codecs=opus` on Chrome. Kept whole; the server normalises. */
  mimeType: string
}

/** A recording in progress. `stop` resolves once the last chunk has been flushed. */
export interface VoiceRecorder {
  stop: () => Promise<VoiceRecording>
  cancel: () => void
}

/**
 * Starts recording, choosing from the candidate types the server offered for the bucket.
 * A parameter so tests can hand in a fake; production always takes the default.
 */
export type StartRecording = (mimeTypes: string[]) => Promise<VoiceRecorder>

export type RecorderFailure = 'unsupported' | 'denied' | 'no_format' | 'failed'

export class RecorderError extends Error {
  readonly reason: RecorderFailure

  constructor(reason: RecorderFailure) {
    super(`Voice recording failed: ${reason}`)
    this.reason = reason
  }
}

/**
 * What the expert is told. Same rule as `GEOLOCATION_EXPLANATIONS`: say what to do differently, or
 * the button just gets pressed again.
 */
export const RECORDER_EXPLANATIONS: Record<RecorderFailure, string> = {
  unsupported:
    'This browser cannot record audio. Use the AXA app on the phone you were dispatched with.',
  denied:
    'No voice note was recorded. AXA needs microphone access to record one. Allow the microphone ' +
    'for this site and press Record again.',
  no_format:
    'This browser cannot record audio in a format AXA accepts. Use the AXA app on the phone you ' +
    'were dispatched with.',
  failed: 'The recording did not complete. Press Record and try again.',
}

/** The first candidate this browser can actually produce, or null if none of them. */
export function pickMimeType(
  mimeTypes: string[],
  isSupported: (type: string) => boolean,
): string | null {
  return mimeTypes.find((type) => isSupported(type)) ?? null
}

/**
 * The real thing. Not exported as the default parameter's identity by accident — `useVoiceNote`
 * takes it as a prop so the hook itself never touches a browser API.
 */
export const startBrowserRecording: StartRecording = async (mimeTypes) => {
  if (
    typeof MediaRecorder === 'undefined' ||
    typeof navigator === 'undefined' ||
    !navigator.mediaDevices?.getUserMedia
  ) {
    throw new RecorderError('unsupported')
  }

  const mimeType = pickMimeType(mimeTypes, (type) => MediaRecorder.isTypeSupported(type))
  if (!mimeType) throw new RecorderError('no_format')

  let stream: MediaStream
  try {
    stream = await navigator.mediaDevices.getUserMedia({ audio: true })
  } catch {
    // NotAllowedError is the common one; anything else here is equally a "no microphone" from the
    // expert's point of view, and the explanation is the same action either way.
    throw new RecorderError('denied')
  }

  // Every path out of here must stop the tracks, or the browser's recording indicator stays lit
  // and the microphone stays open on a phone in someone's pocket.
  function release() {
    for (const track of stream.getTracks()) track.stop()
  }

  let recorder: MediaRecorder
  try {
    recorder = new MediaRecorder(stream, { mimeType })
  } catch {
    release()
    throw new RecorderError('failed')
  }

  const chunks: Blob[] = []
  recorder.ondataavailable = (event) => {
    if (event.data.size > 0) chunks.push(event.data)
  }
  recorder.start()

  return {
    stop: () =>
      new Promise<VoiceRecording>((resolve, reject) => {
        recorder.onstop = () => {
          release()
          // `recorder.mimeType` rather than our request: the browser is entitled to answer with a
          // fuller value (the codec parameter), and the blob must be labelled with what it is.
          resolve({ blob: new Blob(chunks, { type: recorder.mimeType }), mimeType: recorder.mimeType })
        }
        recorder.onerror = () => {
          release()
          reject(new RecorderError('failed'))
        }
        recorder.stop()
      }),
    cancel: () => {
      recorder.onstop = null
      try {
        recorder.stop()
      } catch {
        // Already stopped. Releasing the microphone is the only thing that matters here.
      }
      release()
    },
  }
}

/**
 * A recording, named for the folder it lands in. The random suffix is not decoration: every voice
 * note on an assignment would otherwise arrive at NEXT3 under the same filename, and #5 may yet make
 * that a literal directory rather than an API.
 */
export function recordingToFile(recording: VoiceRecording, id: string): File {
  return new File([recording.blob], `voice-note-${id}${extensionFor(recording.mimeType)}`, {
    type: recording.mimeType,
  })
}

/** A short, filename-safe id. Falls back to a hex slice where `randomUUID` is missing. */
export function shortId(random: () => string = () => globalThis.crypto.randomUUID()): string {
  return random().replace(/-/g, '').slice(0, 8)
}

// Derived from the subtype rather than kept as a lookup table: a second copy of the server's
// extension map is a second thing to keep in step, and `audio/webm` -> `.webm` needs no table. The
// mime type may carry a codec parameter, so cut that off first. This names the *file* the expert
// sends; the blob key is the server's own business and is built from the document id.
export function extensionFor(mimeType: string): string {
  const subtype = mimeType.split(';')[0].trim().toLowerCase().split('/')[1]
  return subtype ? `.${subtype}` : ''
}
