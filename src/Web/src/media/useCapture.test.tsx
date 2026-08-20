import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import type { RgbaImage } from './clarity'
import type { MediaConfig } from './config'
import { DecodeError, type DecodedImage } from './decode'
import { useCapture } from './useCapture'

const PATH = '/api/expert/assignments/00000000-0000-0000-0000-00000000a11c/documents'

const CONFIG: MediaConfig = {
  clarity: { minWidth: 1024, minHeight: 768, blurVarianceThreshold: 100, blurAnalysisMaxEdge: 64 },
  maxFileMb: 15,
  buckets: [
    { bucket: 'insured_car_photo', allowUpload: false, contentTypes: ['image/jpeg', 'image/png'] },
    {
      bucket: 'insured_documents',
      allowUpload: true,
      contentTypes: ['image/jpeg', 'image/png', 'application/pdf'],
    },
  ],
}

function pixels(width: number, height: number, sharp: boolean): RgbaImage {
  const buffer = new Uint8ClampedArray(width * height * 4)
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const value = sharp ? ((x + y) % 2 === 0 ? 0 : 255) : 120
      const at = (y * width + x) * 4
      buffer[at] = value
      buffer[at + 1] = value
      buffer[at + 2] = value
      buffer[at + 3] = 255
    }
  }
  return { width, height, pixels: buffer }
}

/** Stands in for the browser's canvas decode, which jsdom cannot do at all. */
function decoderFor(width: number, height: number, sharp: boolean) {
  return (): Promise<DecodedImage> =>
    Promise.resolve({ width, height, analysis: pixels(32, 24, sharp) })
}

function imageFile(name = 'PLACEHOLDER-photo.jpg') {
  return new File([new Uint8Array([1, 2, 3])], name, { type: 'image/jpeg' })
}

function pdfFile() {
  return new File([new Uint8Array([1, 2, 3])], 'PLACEHOLDER-doc.pdf', { type: 'application/pdf' })
}

function pngFile() {
  return new File([new Uint8Array([1, 2, 3])], 'damage-diagram-abcd1234.png', { type: 'image/png' })
}

describe('useCapture', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn(() =>
      Promise.resolve(
        new Response(JSON.stringify({ id: 'doc-1', bucket: 'insured_car_photo' }), { status: 201 }),
      ),
    )
    vi.stubGlobal('fetch', fetchMock)
  })

  it('holds a passing photo at the confirm screen and uploads nothing yet', async () => {
    // §7.2 item 3: a person looks at the photo before it goes. That is the whole screen.
    const { result } = render({ decode: decoderFor(1600, 1200, true) })

    act(() => result.current.select(imageFile(), 'captured'))

    await waitFor(() => expect(result.current.stage).toBe('confirm'))
    expect(result.current.candidate?.verdict?.ok).toBe(true)
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('posts the metadata parts before the file, and flags the origin', async () => {
    // The order is the server's contract: a streamed upload cannot apply §7.1's capture-only rule
    // to bytes that have already been written, so out-of-order is 400 metadata_must_precede_file.
    const { result } = render({ decode: decoderFor(1600, 1200, true) })

    act(() => result.current.select(imageFile(), 'captured'))
    await waitFor(() => expect(result.current.stage).toBe('confirm'))
    act(() => result.current.confirm())

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(PATH)
    expect(init.method).toBe('POST')

    const body = init.body as FormData
    expect([...body.keys()]).toEqual(['bucket', 'origin', 'file'])
    expect(body.get('bucket')).toBe('insured_car_photo')
    expect(body.get('origin')).toBe('captured')

    // The browser must set multipart/form-data itself: only it knows the boundary.
    const headers = init.headers as Record<string, string>
    expect(headers['Content-Type']).toBeUndefined()
  })

  it('refuses a picked file for a capture-only bucket without asking the server', async () => {
    // §7.1's hard rule. The server refuses it too, but a UI that offers the choice and then fails
    // it has already taught the expert the wrong thing.
    const { result } = render({ decode: decoderFor(1600, 1200, true) })

    act(() => result.current.select(imageFile(), 'uploaded'))

    await waitFor(() => expect(result.current.stage).toBe('rejected'))
    expect(result.current.problem).toContain('taken with the camera')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('accepts a picked file for a bucket that allows upload', async () => {
    const { result } = render({ bucket: 'insured_documents', decode: decoderFor(1600, 1200, true) })

    act(() => result.current.select(imageFile(), 'uploaded'))
    await waitFor(() => expect(result.current.stage).toBe('confirm'))
    act(() => result.current.confirm())

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect((init.body as FormData).get('origin')).toBe('uploaded')
  })

  it('rejects a blurry photo client-side and sends nothing', async () => {
    const { result } = render({ decode: decoderFor(1600, 1200, false) })

    act(() => result.current.select(imageFile(), 'captured'))

    await waitFor(() => expect(result.current.stage).toBe('rejected'))
    expect(result.current.problem).toContain('blurry')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('rejects a photo below the resolution floor and sends nothing', async () => {
    const { result } = render({ decode: decoderFor(640, 480, true) })

    act(() => result.current.select(imageFile(), 'captured'))

    await waitFor(() => expect(result.current.stage).toBe('rejected'))
    expect(result.current.problem).toContain('low-resolution')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('lets a rejected photo be retaken', async () => {
    const { result } = render({ decode: decoderFor(1600, 1200, false) })

    act(() => result.current.select(imageFile(), 'captured'))
    await waitFor(() => expect(result.current.stage).toBe('rejected'))
    act(() => result.current.retake())

    expect(result.current.stage).toBe('idle')
    expect(result.current.candidate).toBeNull()
    expect(result.current.problem).toBeNull()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('skips the gate for a PDF, which has neither dimensions nor blur', async () => {
    const decode = vi.fn(decoderFor(1600, 1200, true))
    const { result } = render({ bucket: 'insured_documents', decode })

    act(() => result.current.select(pdfFile(), 'uploaded'))

    await waitFor(() => expect(result.current.stage).toBe('confirm'))
    expect(result.current.candidate?.verdict).toBeNull()
    expect(decode).not.toHaveBeenCalled()
  })

  it('skips the gate for an image that is not a photograph', async () => {
    // The damage diagram (slice 3.1). It is a PNG, so the MIME type alone would send it through the
    // blur pass — and a vector drawing has no focus to measure, so a low variance would refuse a
    // perfectly good diagram while advising the expert to hold the phone still. The server still
    // applies §7.2's resolution floor to it, which is what the fixed render size is for.
    const decode = vi.fn(decoderFor(1600, 1200, true))
    const { result } = render({ decode })

    act(() => result.current.select(pngFile(), 'captured', { photographic: false }))

    await waitFor(() => expect(result.current.stage).toBe('confirm'))
    expect(result.current.candidate?.verdict).toBeNull()
    expect(decode).not.toHaveBeenCalled()
  })

  it('still gates a photograph when no options are passed', async () => {
    // The default has to stay `true`, or slice 5.1's garage flow and 5.3/6.1's public page silently
    // lose the gate the moment they call `select` the way every existing caller does.
    const decode = vi.fn(decoderFor(1600, 1200, false))
    const { result } = render({ decode })

    act(() => result.current.select(imageFile(), 'captured'))

    await waitFor(() => expect(result.current.stage).toBe('rejected'))
    expect(decode).toHaveBeenCalledTimes(1)
  })

  it('issues one request when Confirm is pressed twice in the same tick', async () => {
    // Same class of bug as the Arrived double-press: `isPending` has not landed in state yet, so
    // only the latch stops the second press. Two uploads means a duplicate photo under the visa.
    const { result } = render({ decode: decoderFor(1600, 1200, true) })

    act(() => result.current.select(imageFile(), 'captured'))
    await waitFor(() => expect(result.current.stage).toBe('confirm'))
    act(() => {
      result.current.confirm()
      result.current.confirm()
    })

    await waitFor(() => expect(result.current.stage).toBe('idle'))
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('reports a server rejection in words, and keeps the photo for another try', async () => {
    fetchMock.mockResolvedValue(new Response('{"error":"image_too_small"}', { status: 400 }))
    const { result } = render({ decode: decoderFor(1600, 1200, true) })

    act(() => result.current.select(imageFile(), 'captured'))
    await waitFor(() => expect(result.current.stage).toBe('confirm'))
    act(() => result.current.confirm())

    await waitFor(() => expect(result.current.problem).not.toBeNull())
    expect(result.current.problem).toContain('low-resolution')
    expect(result.current.problem).not.toContain('image_too_small')
    expect(result.current.stage).toBe('confirm')
  })

  it('tells the caller to refresh only after the upload succeeded', async () => {
    const onUploaded = vi.fn()
    const { result } = render({ decode: decoderFor(1600, 1200, true), onUploaded })

    act(() => result.current.select(imageFile(), 'captured'))
    await waitFor(() => expect(result.current.stage).toBe('confirm'))
    expect(onUploaded).not.toHaveBeenCalled()

    act(() => result.current.confirm())

    await waitFor(() => expect(onUploaded).toHaveBeenCalledTimes(1))
    expect(result.current.candidate).toBeNull()
  })

  it('explains an undecodable file instead of sending it', async () => {
    const { result } = render({
      decode: () => Promise.reject(new DecodeError('unreadable')),
    })

    act(() => result.current.select(imageFile(), 'captured'))

    await waitFor(() => expect(result.current.stage).toBe('rejected'))
    expect(result.current.problem).toContain('could not be opened')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('releases the preview URL when a photo is replaced or retaken', async () => {
    const revokePreviewUrl = vi.fn()
    const { result } = render({
      decode: decoderFor(1600, 1200, true),
      makePreviewUrl: () => 'blob:PLACEHOLDER',
      revokePreviewUrl,
    })

    act(() => result.current.select(imageFile(), 'captured'))
    await waitFor(() => expect(result.current.stage).toBe('confirm'))
    act(() => result.current.retake())

    expect(revokePreviewUrl).toHaveBeenCalledWith('blob:PLACEHOLDER')
  })

  it('ignores a decode that finishes after the photo was already retaken', async () => {
    // A slow decode on a cheap handset must not overwrite a newer selection - the expert would see
    // a verdict belonging to a photo they have already discarded.
    let settle: (decoded: DecodedImage) => void = () => {}
    const slow = () =>
      new Promise<DecodedImage>((resolve) => {
        settle = resolve
      })
    const { result } = render({ decode: slow })

    act(() => result.current.select(imageFile(), 'captured'))
    await waitFor(() => expect(result.current.stage).toBe('assessing'))
    act(() => result.current.retake())
    act(() => settle({ width: 1600, height: 1200, analysis: pixels(32, 24, true) }))

    await Promise.resolve()
    expect(result.current.stage).toBe('idle')
    expect(result.current.candidate).toBeNull()
  })

  function render(
    overrides: Partial<Parameters<typeof useCapture>[0]> & {
      decode?: Parameters<typeof useCapture>[0]['decode']
    } = {},
  ) {
    return renderHook(
      () =>
        useCapture({
          path: PATH,
          bucket: 'insured_car_photo',
          config: CONFIG,
          ...overrides,
        }),
      { wrapper: TestQueryProvider },
    )
  }
})
