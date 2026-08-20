import { describe, expect, it } from 'vitest'
import { extensionFor, pickMimeType, recordingToFile, shortId } from './recorder'

/**
 * The pure half of the voice recorder. Everything below `StartRecording` — permissions, codec
 * negotiation, track teardown — lives in the browser and is proven in the manual pass; jsdom has no
 * `MediaRecorder` at all, so stubbing one would only test the stub.
 */
describe('the voice recorder', () => {
  it('picks the first format this browser can actually produce', () => {
    // The candidates come from the server (`Media.AudioContentTypes`), and the order in that list is
    // the preference order — which is how #10's answer becomes a config edit rather than a change here.
    const supported = (type: string) => type === 'audio/mp4'

    expect(pickMimeType(['audio/webm', 'audio/mp4', 'audio/ogg'], supported)).toBe('audio/mp4')
  })

  it('picks nothing when the browser can produce none of them', () => {
    // Safari before it grew MediaRecorder, and the reason `no_format` is a distinct failure: it is
    // not a permission problem and telling the expert to allow the microphone would waste their time.
    expect(pickMimeType(['audio/webm', 'audio/ogg'], () => false)).toBeNull()
    expect(pickMimeType([], () => true)).toBeNull()
  })

  it('names the file from the container the recorder actually used', () => {
    const file = recordingToFile(
      { blob: new Blob([new Uint8Array([1, 2, 3])]), mimeType: 'audio/webm;codecs=opus' },
      'abcd1234',
    )

    expect(file.name).toBe('voice-note-abcd1234.webm')
    // The codec parameter is kept on the File: the server is what strips it, and the row must record
    // what was validated. Dropping it here would mean the browser path never exercises that.
    expect(file.type).toBe('audio/webm;codecs=opus')
    expect(file.size).toBe(3)
  })

  it('derives the extension from the subtype rather than a lookup table', () => {
    // A second copy of the server's extension map is a second thing to keep in step for no benefit.
    expect(extensionFor('audio/webm;codecs=opus')).toBe('.webm')
    expect(extensionFor('AUDIO/MP4')).toBe('.mp4')
    expect(extensionFor('audio/ogg')).toBe('.ogg')
    expect(extensionFor('nonsense')).toBe('')
  })

  it('makes a short filename-safe id', () => {
    // Several voice notes on one claim must not all arrive at NEXT3 as the same filename — and #5
    // may yet make that folder a literal directory rather than an API.
    const id = shortId(() => '0189d3f6-1a2b-7c3d-9e4f-5a6b7c8d9e0f')

    expect(id).toBe('0189d3f6')
    expect(id).toMatch(/^[0-9a-f]{8}$/)
  })
})
