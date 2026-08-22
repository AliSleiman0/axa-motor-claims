import type { MediaConfig } from '../config'
import {
  PNG_CONTENT_TYPE,
  STANDARD_RENDER,
  SVG_RENDER_EXPLANATIONS,
  SvgRenderError,
  fitsFloor,
  renderSvgToFile,
  serializeSvg,
  type SvgRenderFailure,
  type SvgRenderer,
} from '../png'

/**
 * The diagram's half of what used to be one file (generalized into `../png.ts` in slice 4.2, when the
 * officer's approval image needed the same canvas dance).
 *
 * What stays here is only what is *about the diagram*: the name of its export, and the runtime floor
 * guard the panel renders an explanation from. Everything mechanical — serializing, rasterizing, the
 * injectable renderer, the error type — moved, so there is one canvas path in the application rather
 * than two that can drift.
 */
export const DIAGRAM_RENDER = STANDARD_RENDER

export const DIAGRAM_CONTENT_TYPE = PNG_CONTENT_TYPE

/**
 * Kept as aliases rather than rewritten at every call site: `DiagramPanel` and its tests talk about a
 * `DiagramRenderer`, and renaming a type across a working panel buys nothing this slice needs.
 */
export type DiagramRenderer = SvgRenderer
export type DiagramFailure = SvgRenderFailure
export { SvgRenderError as DiagramRenderError }
export const DIAGRAM_EXPLANATIONS = SVG_RENDER_EXPLANATIONS

/** Can a `DIAGRAM_RENDER`-sized export clear the configured floor? See `fitsFloor`. */
export function diagramFitsFloor(config: MediaConfig | undefined): boolean {
  return fitsFloor(config, DIAGRAM_RENDER)
}

/** The live element as markup, at the export size. */
export function serializeDiagram(
  svg: SVGSVGElement,
  serialize?: (node: Node) => string,
): string {
  return serializeSvg(svg, DIAGRAM_RENDER, serialize)
}

/** The export, as the `File` `useCapture.select` takes. */
export function renderDiagramToFile(
  svgMarkup: string,
  id: string,
  render?: DiagramRenderer,
): Promise<File> {
  // Suffixed for the same reason a voice note is: several diagrams on one claim must not all arrive
  // at NEXT3 under one filename, and #5 may yet make that folder a literal directory.
  return renderSvgToFile(svgMarkup, DIAGRAM_RENDER, `damage-diagram-${id}.png`, render)
}
