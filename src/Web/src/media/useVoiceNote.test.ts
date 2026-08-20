import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { RecorderError, type StartRecording, type VoiceRecorder } from './recorder'
import { useVoiceNote } from './useVoiceNote'

const MIME_TYPES = ['audio/webm', 'audio/mp4', 'audio/ogg']

/** A recording that yields the given bytes when stopped — the seam `StartRecording` exists for. */
function fakeRecorder(bytes: number[], mimeType = 'audio/webm;codecs=opus') {
  const cancel = vi.fn()
  const recorder: VoiceRecorder = {
    stop: () => Promise.resolve({ blob: new Blob([new Uint8Array(bytes)]), mimeType }),
    cancel,
  }
  const start: StartRecording = vi.fn(() => Promise.resolve(recorder))
  return { start, cancel }
}

describe('useVoiceNote', () => {
  it('hands the finished recording over as a File', async () => {
    // The hook's whole job: produce a File and let `useCapture` take it from there. It never uploads
    // anything itself — that would duplicate the pipeline slice 5.1 and 5.3/6.1 have to reuse.
    const onRecorded = vi.fn()
    const { start } = fakeRecorder([1, 2, 3, 4])
    const { result } = render({ onRecorded, start })

    act(() => result.current.start())
    await waitFor(() => expect(result.current.recording).toBe(true))

    act(() => result.current.stop())
    await waitFor(() => expect(onRecorded).toHaveBeenCalledTimes(1))

    const file = onRecorded.mock.calls[0][0] as File
    expect(file.type).toBe('audio/webm;codecs=opus')
    expect(file.name).toBe('voice-note-abcd1234.webm')
    expect(result.current.recording).toBe(false)
    expect(result.current.problem).toBeNull()
  })

  it('offers the recorder the formats the server accepts', async () => {
    const { start } = fakeRecorder([1])
    const { result } = render({ start })

    act(() => result.current.start())
    await waitFor(() => expect(result.current.recording).toBe(true))

    expect(start).toHaveBeenCalledWith(MIME_TYPES)
  })

  it('explains a refused microphone rather than reporting nothing', async () => {
    const start: StartRecording = () => Promise.reject(new RecorderError('denied'))
    const onRecorded = vi.fn()
    const { result } = render({ onRecorded, start })

    act(() => result.current.start())

    await waitFor(() => expect(result.current.problem).not.toBeNull())
    expect(result.current.problem).toContain('microphone access')
    expect(result.current.recording).toBe(false)
    expect(onRecorded).not.toHaveBeenCalled()
  })

  it('explains a browser that cannot record in any accepted format', async () => {
    // Distinct from a permission refusal: no amount of allowing the microphone fixes it.
    const start: StartRecording = () => Promise.reject(new RecorderError('no_format'))
    const { result } = render({ start })

    act(() => result.current.start())

    await waitFor(() => expect(result.current.problem).not.toBeNull())
    expect(result.current.problem).toContain('format AXA accepts')
  })

  it('refuses an empty recording instead of posting one the server will reject', async () => {
    // The server answers `file_empty`; saying it here tells the expert the recording did not happen,
    // rather than that AXA turned it down.
    const onRecorded = vi.fn()
    const { start } = fakeRecorder([])
    const { result } = render({ onRecorded, start })

    act(() => result.current.start())
    await waitFor(() => expect(result.current.recording).toBe(true))
    act(() => result.current.stop())

    await waitFor(() => expect(result.current.problem).not.toBeNull())
    expect(onRecorded).not.toHaveBeenCalled()
  })

  it('opens one microphone when Record is pressed twice in the same tick', async () => {
    // 1.5's lesson, fourth slice running. `recording` has not landed in state yet for the second
    // press, so only the latch stops it — and a second getUserMedia leaves the first recorder
    // running with nothing holding a reference to stop it.
    const { start } = fakeRecorder([1, 2])
    const { result } = render({ start })

    act(() => {
      result.current.start()
      result.current.start()
    })

    await waitFor(() => expect(result.current.recording).toBe(true))
    expect(start).toHaveBeenCalledTimes(1)
  })

  it('ignores Stop when nothing is recording', async () => {
    const onRecorded = vi.fn()
    const { start } = fakeRecorder([1])
    const { result } = render({ onRecorded, start })

    act(() => result.current.stop())

    await Promise.resolve()
    expect(onRecorded).not.toHaveBeenCalled()
    expect(result.current.problem).toBeNull()
  })

  function render(overrides: Partial<Parameters<typeof useVoiceNote>[0]> = {}) {
    return renderHook(() =>
      useVoiceNote({
        mimeTypes: MIME_TYPES,
        onRecorded: vi.fn(),
        makeId: () => 'abcd1234',
        ...overrides,
      }),
    )
  }
})
