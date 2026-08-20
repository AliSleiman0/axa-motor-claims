import { describe, expect, it } from 'vitest'
import {
  assessClarity,
  downscaleTo,
  laplacianVariance,
  toGrayscale,
  type ClarityThresholds,
  type RgbaImage,
} from './clarity'

/** Builds an RGBA image from a matrix of gray levels, so a test can state pixels as numbers. */
function grayImage(rows: number[][]): RgbaImage {
  const height = rows.length
  const width = rows[0].length
  const pixels = new Uint8ClampedArray(width * height * 4)
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const value = rows[y][x]
      const at = (y * width + x) * 4
      pixels[at] = value
      pixels[at + 1] = value
      pixels[at + 2] = value
      pixels[at + 3] = 255
    }
  }
  return { width, height, pixels }
}

function flat(width: number, height: number, value: number): RgbaImage {
  return grayImage(Array.from({ length: height }, () => Array.from({ length: width }, () => value)))
}

function checkerboard(width: number, height: number): RgbaImage {
  return grayImage(
    Array.from({ length: height }, (_unusedRow, y) =>
      Array.from({ length: width }, (_unusedColumn, x) => ((x + y) % 2 === 0 ? 0 : 255)),
    ),
  )
}

/**
 * The 4x4 whose Laplacian variance is computed by hand in the test below. A single bright pixel,
 * chosen because every response is then a small integer that can be written out longhand.
 */
const SINGLE_SPIKE = grayImage([
  [0, 0, 0, 0],
  [0, 10, 0, 0],
  [0, 0, 0, 0],
  [0, 0, 0, 0],
])

/** Variance of SINGLE_SPIKE, derived in the test that pins it. */
const SPIKE_VARIANCE = 425

function thresholds(overrides: Partial<ClarityThresholds> = {}): ClarityThresholds {
  return {
    minWidth: 4,
    minHeight: 4,
    blurVarianceThreshold: 100,
    blurAnalysisMaxEdge: 64,
    ...overrides,
  }
}

describe('toGrayscale', () => {
  it('collapses RGBA to one byte per pixel and leaves a neutral gray unchanged', () => {
    const gray = toGrayscale(flat(2, 2, 10))

    expect(gray.width).toBe(2)
    expect(gray.height).toBe(2)
    expect(gray.pixels.length).toBe(4)
    expect(Array.from(gray.pixels)).toEqual([10, 10, 10, 10])
  })

  it('weights the channels by luma rather than averaging them', () => {
    // Pure green is much brighter to the eye than pure blue; a flat mean would call them equal.
    const green = grayImage([[0]])
    green.pixels.set([0, 255, 0, 255])
    const blue = grayImage([[0]])
    blue.pixels.set([0, 0, 255, 255])

    expect(toGrayscale(green).pixels[0]).toBeGreaterThan(toGrayscale(blue).pixels[0])
  })
})

describe('laplacianVariance', () => {
  // Worked longhand so this asserts arithmetic rather than the implementation's own output — a test
  // that feeds the code's result back to itself can never go red (slice 2.4's lesson).
  //
  // Kernel [0 1 0; 1 -4 1; 0 1 0] over the four interior pixels of SINGLE_SPIKE:
  //   (1,1) center 10, neighbours all 0  -> 0 - 40 = -40
  //   (2,1) center  0, left is the 10    -> 10 - 0 =  10
  //   (1,2) center  0, above is the 10   -> 10 - 0 =  10
  //   (2,2) center  0, neighbours all 0  ->  0 - 0 =   0
  // mean = -5; deviations -35, 15, 15, 5; squares 1225 + 225 + 225 + 25 = 1700; 1700 / 4 = 425.
  it('matches a hand-computed variance', () => {
    expect(laplacianVariance(toGrayscale(SINGLE_SPIKE))).toBeCloseTo(SPIKE_VARIANCE, 6)
  })

  it('is zero for a flat image, which has no edges at all', () => {
    expect(laplacianVariance(toGrayscale(flat(8, 8, 120)))).toBe(0)
  })

  it('is large for a checkerboard, which is nothing but edges', () => {
    expect(laplacianVariance(toGrayscale(checkerboard(8, 8)))).toBeGreaterThan(100_000)
  })

  it('is zero when the image is too small to have an interior pixel', () => {
    expect(laplacianVariance(toGrayscale(flat(2, 2, 200)))).toBe(0)
  })
})

describe('downscaleTo', () => {
  it('caps the longest edge and keeps the aspect ratio', () => {
    const scaled = downscaleTo(flat(100, 50, 30), 10)

    expect(scaled.width).toBe(10)
    expect(scaled.height).toBe(5)
  })

  it('caps the longest edge when the image is portrait', () => {
    const scaled = downscaleTo(flat(50, 100, 30), 10)

    expect(scaled.width).toBe(5)
    expect(scaled.height).toBe(10)
  })

  it('never upscales an image that is already within the cap', () => {
    const scaled = downscaleTo(flat(8, 8, 30), 64)

    expect(scaled.width).toBe(8)
    expect(scaled.height).toBe(8)
  })

  it('keeps every edge at least one pixel', () => {
    const scaled = downscaleTo(flat(1000, 3, 30), 10)

    expect(scaled.width).toBe(10)
    expect(scaled.height).toBe(1)
  })
})

describe('assessClarity', () => {
  it('passes a sharp image that clears both floors', () => {
    const verdict = assessClarity(checkerboard(8, 8), checkerboard(8, 8), thresholds())

    expect(verdict.ok).toBe(true)
    expect(verdict.failure).toBeNull()
    expect(verdict.width).toBe(8)
    expect(verdict.height).toBe(8)
  })

  it('rejects a flat image as too blurry', () => {
    const verdict = assessClarity(flat(8, 8, 120), flat(8, 8, 120), thresholds())

    expect(verdict.ok).toBe(false)
    expect(verdict.failure).toBe('too_blurry')
    expect(verdict.variance).toBe(0)
  })

  // design.md §7.2 item 2: "below Clarity.BlurVarianceThreshold -> retry prompt". Below, not at.
  it('passes when the variance sits exactly on the threshold', () => {
    const verdict = assessClarity(
      SINGLE_SPIKE,
      SINGLE_SPIKE,
      thresholds({ blurVarianceThreshold: SPIKE_VARIANCE }),
    )

    expect(verdict.ok).toBe(true)
    expect(verdict.failure).toBeNull()
  })

  it('rejects when the variance sits one unit below the threshold', () => {
    const verdict = assessClarity(
      SINGLE_SPIKE,
      SINGLE_SPIKE,
      thresholds({ blurVarianceThreshold: SPIKE_VARIANCE + 1 }),
    )

    expect(verdict.ok).toBe(false)
    expect(verdict.failure).toBe('too_blurry')
  })

  it('rejects an image below the resolution floor', () => {
    const verdict = assessClarity(
      checkerboard(8, 8),
      checkerboard(8, 8),
      thresholds({ minWidth: 16, minHeight: 16 }),
    )

    expect(verdict.ok).toBe(false)
    expect(verdict.failure).toBe('too_small')
  })

  it('rejects an image below the floor on height alone', () => {
    const verdict = assessClarity(
      checkerboard(8, 8),
      checkerboard(8, 8),
      thresholds({ minWidth: 8, minHeight: 16 }),
    )

    expect(verdict.failure).toBe('too_small')
  })

  // One message, and the actionable one: "move closer" is useless advice for a 640x480 sensor.
  it('reports the resolution failure when an image is both small and blurry', () => {
    const verdict = assessClarity(
      flat(8, 8, 120),
      flat(8, 8, 120),
      thresholds({ minWidth: 16, minHeight: 16 }),
    )

    expect(verdict.failure).toBe('too_small')
  })

  // The discriminating test for decision 2, in the shape the decoder actually produces: a 1200x900
  // photo whose analysis buffer has already been bounded to 64x48 so a 48 MP handset never
  // materialises a ~192 MB RGBA array. Were the floor measured on the buffer instead of on `native`,
  // this would be refused as too small — telling the expert their 1.1 MP photo is too
  // low-resolution, which is both wrong and unfixable from where they are standing.
  it('measures the resolution floor at native size, not at the analysis size', () => {
    const verdict = assessClarity(
      { width: 1200, height: 900 },
      checkerboard(64, 48),
      thresholds({ minWidth: 1024, minHeight: 768, blurAnalysisMaxEdge: 64 }),
    )

    expect(verdict.failure).not.toBe('too_small')
    expect(verdict.width).toBe(1200)
    expect(verdict.height).toBe(900)
  })
})
