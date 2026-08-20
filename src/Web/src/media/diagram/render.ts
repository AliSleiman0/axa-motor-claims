import type { MediaConfig } from '../config'

/**
 * The size the diagram is flattened to, and the reason it is a constant rather than the SVG's own
 * 400×300.
 *
 * `damage_diagram` is an **image** bucket, so design.md §7.2 item 1's resolution floor is applied
 * to the export by the server exactly as it is to a photograph. A diagram rendered at its natural
 * size would be refused with `image_too_small` every single time. 4:3, matching `CAR_VIEWBOX`, so
 * nothing is stretched or letterboxed.
 *
 * If `Clarity.MinWidth`/`MinHeight` are ever raised past this, **every diagram in the application
 * stops being accepted** — so the pairing is guarded from the one side that can read the configured
 * value: `TheClarityFloorAdmitsADamageDiagram` in `src/Api.Tests/Integration`. A jsdom test cannot
 * see `appsettings.Placeholders.json`, so `diagramFitsFloor` below is the runtime half: it refuses
 * loudly on screen rather than letting the expert draw something the server will bounce.
 */
export const DIAGRAM_RENDER = { width: 1600, height: 1200 } as const

export const DIAGRAM_CONTENT_TYPE = 'image/png'

/** Flattens serialized SVG markup to PNG bytes. Injected, because jsdom has no canvas at all. */
export type DiagramRenderer = (
  svgMarkup: string,
  size: { width: number; height: number },
) => Promise<Blob>

export type DiagramFailure = 'unsupported' | 'failed'

export class DiagramRenderError extends Error {
  readonly reason: DiagramFailure

  constructor(reason: DiagramFailure) {
    super(`Diagram render failed: ${reason}`)
    this.reason = reason
  }
}

export const DIAGRAM_EXPLANATIONS: Record<DiagramFailure, string> = {
  unsupported:
    'This browser cannot turn the diagram into an image. Use the AXA app on the phone you were ' +
    'dispatched with.',
  failed: 'The diagram could not be prepared for sending. Try again.',
}

/**
 * Can a `DIAGRAM_RENDER`-sized export clear the configured floor?
 *
 * Pure, so the panel can ask before drawing anything. Returns true while the config is still in
 * flight — the panel's own `configLoaded` rung covers that case, and answering "no" here would show
 * a misconfiguration alert for a request that simply has not landed yet.
 */
export function diagramFitsFloor(config: MediaConfig | undefined): boolean {
  if (!config) return true
  return (
    DIAGRAM_RENDER.width >= config.clarity.minWidth &&
    DIAGRAM_RENDER.height >= config.clarity.minHeight
  )
}

/**
 * The live element as markup. Explicit width and height are re-stamped on the root: a `viewBox`
 * alone leaves the intrinsic size undefined, and a browser asked to rasterize such an image
 * produces nothing at all in some engines.
 */
export function serializeDiagram(
  svg: SVGSVGElement,
  serialize: (node: Node) => string = (node) => new XMLSerializer().serializeToString(node),
): string {
  const clone = svg.cloneNode(true) as SVGSVGElement
  clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg')
  clone.setAttribute('width', String(DIAGRAM_RENDER.width))
  clone.setAttribute('height', String(DIAGRAM_RENDER.height))
  return serialize(clone)
}

/**
 * Draws the markup into a canvas and reads PNG bytes back out.
 *
 * The SVG travels as a data URL rather than a blob URL: an `<img>` fed a *blob* of SVG taints the
 * canvas in some engines, and a tainted canvas fails `toBlob` — which would surface as "the diagram
 * could not be prepared" on exactly the handsets that behave that way and nowhere else.
 */
export const browserDiagramRenderer: DiagramRenderer = async (svgMarkup, size) => {
  if (typeof document === 'undefined') throw new DiagramRenderError('unsupported')

  const canvas = document.createElement('canvas')
  canvas.width = size.width
  canvas.height = size.height

  const context = canvas.getContext('2d')
  if (!context) throw new DiagramRenderError('unsupported')

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
      else reject(new DiagramRenderError('failed'))
    }, DIAGRAM_CONTENT_TYPE)
  })
}

/** The export, as the `File` `useCapture.select` takes. */
export async function renderDiagramToFile(
  svgMarkup: string,
  id: string,
  render: DiagramRenderer = browserDiagramRenderer,
): Promise<File> {
  const blob = await render(svgMarkup, DIAGRAM_RENDER)
  // Suffixed for the same reason a voice note is: several diagrams on one claim must not all arrive
  // at NEXT3 under one filename, and #5 may yet make that folder a literal directory.
  return new File([blob], `damage-diagram-${id}.png`, { type: DIAGRAM_CONTENT_TYPE })
}

function loadImage(source: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image()
    image.onload = () => resolve(image)
    image.onerror = () => reject(new DiagramRenderError('failed'))
    image.src = source
  })
}
