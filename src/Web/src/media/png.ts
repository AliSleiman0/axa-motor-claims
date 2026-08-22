import type { MediaConfig } from './config'

export const PNG_CONTENT_TYPE = 'image/png'

export interface RenderSize {
  width: number
  height: number
}

/**
 * The size anything this module flattens is rendered at, and the reason it is a constant.
 *
 * Both callers upload into an **image** bucket, so design.md §7.2 item 1's resolution floor is applied
 * to the export by the server exactly as it is to a photograph — a drawing rendered at its natural
 * size would be refused with `image_too_small` every single time. 4:3, matching `CAR_VIEWBOX`, so
 * nothing is stretched or letterboxed.
 *
 * If `Clarity.MinWidth`/`MinHeight` are ever raised past this, **every diagram and every approval
 * image in the application stops being accepted** — so the pairing is guarded from the one side that
 * can read the configured value: `TheClarityFloorAdmitsADamageDiagram` in `src/Api.Tests/Integration`.
 * A jsdom test cannot see `appsettings.Placeholders.json`, so `fitsFloor` below is the runtime half.
 */
export const STANDARD_RENDER: RenderSize = { width: 1600, height: 1200 }

/** Flattens serialized SVG markup to PNG bytes. Injected, because jsdom has no canvas at all. */
export type SvgRenderer = (svgMarkup: string, size: RenderSize) => Promise<Blob>

export type SvgRenderFailure = 'unsupported' | 'failed'

export class SvgRenderError extends Error {
  readonly reason: SvgRenderFailure

  constructor(reason: SvgRenderFailure) {
    super(`SVG render failed: ${reason}`)
    this.reason = reason
  }
}

export const SVG_RENDER_EXPLANATIONS: Record<SvgRenderFailure, string> = {
  unsupported:
    'This browser cannot turn the drawing into an image. Use the AXA app on the phone you were ' +
    'dispatched with.',
  failed: 'The image could not be prepared for sending. Try again.',
}

/**
 * Can a render of this size clear the configured floor?
 *
 * Pure, so a panel can ask before drawing anything. Returns true while the config is still in
 * flight — the panel's own `configLoaded` rung covers that case, and answering "no" here would show
 * a misconfiguration alert for a request that simply has not landed yet.
 */
export function fitsFloor(config: MediaConfig | undefined, size: RenderSize): boolean {
  if (!config) return true
  return size.width >= config.clarity.minWidth && size.height >= config.clarity.minHeight
}

/**
 * A live element as markup. Explicit width and height are re-stamped on the root: a `viewBox` alone
 * leaves the intrinsic size undefined, and a browser asked to rasterize such an image produces
 * nothing at all in some engines.
 */
export function serializeSvg(
  svg: SVGSVGElement,
  size: RenderSize,
  serialize: (node: Node) => string = (node) => new XMLSerializer().serializeToString(node),
): string {
  const clone = svg.cloneNode(true) as SVGSVGElement
  clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg')
  clone.setAttribute('width', String(size.width))
  clone.setAttribute('height', String(size.height))
  return serialize(clone)
}

/**
 * Draws the markup into a canvas and reads PNG bytes back out.
 *
 * The SVG travels as a data URL rather than a blob URL: an `<img>` fed a *blob* of SVG taints the
 * canvas in some engines, and a tainted canvas fails `toBlob` — which would surface as "the image
 * could not be prepared" on exactly the handsets that behave that way and nowhere else.
 */
export const browserSvgRenderer: SvgRenderer = async (svgMarkup, size) => {
  if (typeof document === 'undefined') throw new SvgRenderError('unsupported')

  const canvas = document.createElement('canvas')
  canvas.width = size.width
  canvas.height = size.height

  const context = canvas.getContext('2d')
  if (!context) throw new SvgRenderError('unsupported')

  const image = await loadImage(
    `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svgMarkup)}`,
  )

  // The SVG paints its own white background, but the canvas starts transparent and a failed draw
  // would otherwise produce a fully transparent PNG that still passes every dimension check.
  context.fillStyle = '#ffffff'
  context.fillRect(0, 0, size.width, size.height)
  context.drawImage(image, 0, 0, size.width, size.height)

  return new Promise<Blob>((resolve, reject) => {
    canvas.toBlob((blob) => {
      if (blob) resolve(blob)
      else reject(new SvgRenderError('failed'))
    }, PNG_CONTENT_TYPE)
  })
}

/**
 * The export, as the `File` `useCapture.select` and `uploadDocument` both take.
 *
 * `size` is passed through to the renderer untouched, so a test can assert the exact object it was
 * called with rather than a copy of its fields.
 */
export async function renderSvgToFile(
  svgMarkup: string,
  size: RenderSize,
  fileName: string,
  render: SvgRenderer = browserSvgRenderer,
): Promise<File> {
  const blob = await render(svgMarkup, size)
  return new File([blob], fileName, { type: PNG_CONTENT_TYPE })
}

function loadImage(source: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image()
    image.onload = () => resolve(image)
    image.onerror = () => reject(new SvgRenderError('failed'))
    image.src = source
  })
}
