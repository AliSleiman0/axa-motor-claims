import type { RgbaImage } from './clarity'

/**
 * A decoded image as the clarity gate wants it: the file's **native** dimensions, plus a bounded
 * copy of its pixels to analyse.
 *
 * The split is not cosmetic. A 48 MP phone photo is 8000x6000, and one RGBA byte array at that size
 * is ~192 MB — enough to kill the tab on the handset of an expert standing at a crash site. The
 * analysis buffer is capped at `maxEdge` on the way out of the decoder, so that array is never
 * allocated, while the resolution floor still gets the real dimensions to judge (§7.2 item 1).
 */
export interface DecodedImage {
  width: number
  height: number
  analysis: RgbaImage
}

export type DecodeFailure = 'unsupported' | 'unreadable'

export class DecodeError extends Error {
  readonly reason: DecodeFailure

  constructor(reason: DecodeFailure) {
    super(`Image decode failed: ${reason}`)
    this.reason = reason
  }
}

export const DECODE_EXPLANATIONS: Record<DecodeFailure, string> = {
  unsupported:
    'This browser cannot check photo quality. Use the AXA app on the phone you were dispatched ' +
    'with.',
  unreadable:
    'This file could not be opened as a photo. Take the picture again with the camera rather than ' +
    'attaching a file.',
}

export type ImageDecoder = (file: Blob, maxEdge: number) => Promise<DecodedImage>

/**
 * Decodes with the browser, scaling into a small canvas.
 *
 * `createImageBitmap` holds its pixels outside the JS heap and is closed immediately, so the only
 * buffer this allocates is the bounded one `getImageData` returns.
 */
async function browserDecoder(file: Blob, maxEdge: number): Promise<DecodedImage> {
  if (typeof createImageBitmap !== 'function' || typeof document === 'undefined') {
    throw new DecodeError('unsupported')
  }

  let bitmap: ImageBitmap
  try {
    bitmap = await createImageBitmap(file)
  } catch {
    // A file that is not an image the browser can read. The server refuses these too, by its
    // bytes rather than by its content type — this is the client half of the same judgement.
    throw new DecodeError('unreadable')
  }

  try {
    const { width, height } = bitmap
    const scale = Math.min(1, maxEdge / Math.max(width, height))
    const analysisWidth = Math.max(1, Math.round(width * scale))
    const analysisHeight = Math.max(1, Math.round(height * scale))

    const canvas = document.createElement('canvas')
    canvas.width = analysisWidth
    canvas.height = analysisHeight

    const context = canvas.getContext('2d')
    if (!context) throw new DecodeError('unsupported')

    context.drawImage(bitmap, 0, 0, analysisWidth, analysisHeight)
    const data = context.getImageData(0, 0, analysisWidth, analysisHeight)

    return {
      width,
      height,
      analysis: { width: analysisWidth, height: analysisHeight, pixels: data.data },
    }
  } finally {
    bitmap.close()
  }
}

/**
 * The `decode` parameter exists so a test can hand in a stub; production always takes the default.
 * Same shape as `requestPosition`'s injectable `geolocation` — jsdom has no canvas at all, so this
 * is what keeps the gate testable without pulling in a native `canvas` package.
 */
export function decodeImage(
  file: Blob,
  maxEdge: number,
  decode: ImageDecoder = browserDecoder,
): Promise<DecodedImage> {
  return decode(file, maxEdge)
}

/** E4 renders the candidate full-bleed, which needs an object URL rather than the raw File. */
export function createPreviewUrl(
  file: Blob,
  make: (blob: Blob) => string = URL.createObjectURL,
): string {
  return make(file)
}

/**
 * Object URLs pin their blob in memory until revoked, and E3 is a screen an expert stays on while
 * filling four buckets — so every preview that is replaced or abandoned has to be released.
 */
export function revokePreviewUrl(
  url: string,
  revoke: (url: string) => void = URL.revokeObjectURL,
): void {
  revoke(url)
}
