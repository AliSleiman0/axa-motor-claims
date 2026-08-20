/**
 * design.md §7.2's clarity gate — the pure core, deliberately free of the DOM.
 *
 * Everything here is arithmetic over pixel buffers, so the whole gate is testable under jsdom,
 * which has no canvas (and no `canvas` package is installed). Decoding a File into pixels is
 * `decode.ts`'s job; this module never touches a browser API.
 *
 * §7.2 is explicit that this is **not ML** (#9): a resolution floor, a Laplacian blur-variance
 * threshold, and a human confirm screen.
 */

/** design.md Appendix A's `Clarity` section (#9), served by `GET /api/config/media`. */
export interface ClarityThresholds {
  minWidth: number
  minHeight: number
  blurVarianceThreshold: number
  /**
   * The longest edge the blur pass runs at. Laplacian variance scales with resolution, so without
   * a fixed analysis size the same photo scores differently on a 12 MP and a 48 MP handset and the
   * threshold would mean a different thing on every device. Ships beside the threshold in the
   * placeholder config so the two move together when #9 is answered.
   */
  blurAnalysisMaxEdge: number
}

export type ClarityFailure = 'too_small' | 'too_blurry'

/** RGBA, four bytes per pixel — the shape `CanvasRenderingContext2D.getImageData` returns. */
export interface RgbaImage {
  width: number
  height: number
  pixels: Uint8ClampedArray
}

/** One byte per pixel. */
export interface GrayImage {
  width: number
  height: number
  pixels: Uint8ClampedArray
}

export interface ClarityVerdict {
  ok: boolean
  failure: ClarityFailure | null
  /** Native dimensions, not the analysis ones — this is what the server re-checks (§7.2 item 5). */
  width: number
  height: number
  variance: number
}

/**
 * What E4 shows on a refusal. It has to say what to do differently, or the expert re-takes the
 * same photo from the same place and the gate becomes something to fight rather than obey.
 */
export const CLARITY_EXPLANATIONS: Record<ClarityFailure, string> = {
  too_small:
    'This photo is too low-resolution for AXA to assess the damage. Use the rear camera at full ' +
    'quality rather than a screenshot or a forwarded copy, and take it again.',
  too_blurry:
    'This photo is too blurry for AXA to assess the damage. Hold the phone still, let the camera ' +
    'focus on the damage, and take it again.',
}

/** Rec. 601 luma. The three weights sum to exactly 1, so a neutral gray survives unchanged. */
export function toGrayscale(image: RgbaImage): GrayImage {
  const pixels = new Uint8ClampedArray(image.width * image.height)
  for (let index = 0; index < pixels.length; index += 1) {
    const at = index * 4
    pixels[index] =
      0.299 * image.pixels[at] + 0.587 * image.pixels[at + 1] + 0.114 * image.pixels[at + 2]
  }
  return { width: image.width, height: image.height, pixels }
}

/**
 * Nearest-neighbour box reduction to a bounded longest edge. Never upscales: an image already
 * within the cap is returned untouched, because inventing pixels would invent edges and hand a
 * blurry photo a passing variance.
 */
export function downscaleTo(image: RgbaImage, maxEdge: number): RgbaImage {
  const longest = Math.max(image.width, image.height)
  if (longest <= maxEdge) return image

  const scale = maxEdge / longest
  const width = Math.max(1, Math.round(image.width * scale))
  const height = Math.max(1, Math.round(image.height * scale))
  const pixels = new Uint8ClampedArray(width * height * 4)

  for (let y = 0; y < height; y += 1) {
    const sourceY = Math.min(image.height - 1, Math.floor((y * image.height) / height))
    for (let x = 0; x < width; x += 1) {
      const sourceX = Math.min(image.width - 1, Math.floor((x * image.width) / width))
      const from = (sourceY * image.width + sourceX) * 4
      const to = (y * width + x) * 4
      pixels[to] = image.pixels[from]
      pixels[to + 1] = image.pixels[from + 1]
      pixels[to + 2] = image.pixels[from + 2]
      pixels[to + 3] = image.pixels[from + 3]
    }
  }

  return { width, height, pixels }
}

/**
 * Variance of the 3x3 Laplacian response [0 1 0; 1 -4 1; 0 1 0] over the interior pixels.
 *
 * A sharp image is full of abrupt intensity changes, so the responses spread out; a blurred one has
 * gentle gradients and they cluster near zero. An image with no interior pixel scores 0, which the
 * resolution floor would have rejected first in any real call.
 */
export function laplacianVariance(gray: GrayImage): number {
  const { width, height, pixels } = gray
  if (width < 3 || height < 3) return 0

  const count = (width - 2) * (height - 2)
  const responses = new Float64Array(count)
  let total = 0
  let at = 0

  for (let y = 1; y < height - 1; y += 1) {
    for (let x = 1; x < width - 1; x += 1) {
      const center = y * width + x
      const response =
        pixels[center - width] +
        pixels[center + width] +
        pixels[center - 1] +
        pixels[center + 1] -
        4 * pixels[center]
      responses[at] = response
      total += response
      at += 1
    }
  }

  const mean = total / count
  let sumOfSquares = 0
  for (let index = 0; index < count; index += 1) {
    const deviation = responses[index] - mean
    sumOfSquares += deviation * deviation
  }
  return sumOfSquares / count
}

/** Just the numbers the resolution floor needs — a full pixel buffer is not required to check it. */
export interface ImageDimensions {
  width: number
  height: number
}

/**
 * The gate itself (§7.2 items 1 and 2).
 *
 * Takes the dimensions and the pixels **separately** on purpose. The floor is a fact about the file
 * the expert produced, so it is measured at `native` size; the blur pass only ever needs a bounded
 * copy, so `analysis` may already be downscaled — which is what keeps a 48 MP photo from being
 * materialised as a ~192 MB RGBA buffer on a handset. `downscaleTo` still runs here and is a no-op
 * when the buffer is already within the cap, so the threshold means the same thing either way.
 *
 * Order is deliberate: a genuinely small photo is told the one thing it can act on, rather than
 * being called blurry as well.
 */
export function assessClarity(
  native: ImageDimensions,
  analysis: RgbaImage,
  thresholds: ClarityThresholds,
): ClarityVerdict {
  if (native.width < thresholds.minWidth || native.height < thresholds.minHeight) {
    return {
      ok: false,
      failure: 'too_small',
      width: native.width,
      height: native.height,
      variance: 0,
    }
  }

  const variance = laplacianVariance(
    toGrayscale(downscaleTo(analysis, thresholds.blurAnalysisMaxEdge)),
  )

  const sharp = variance >= thresholds.blurVarianceThreshold
  return {
    ok: sharp,
    failure: sharp ? null : 'too_blurry',
    width: native.width,
    height: native.height,
    variance,
  }
}
