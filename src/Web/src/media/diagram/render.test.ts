import { describe, expect, it, vi } from 'vitest'
import type { MediaConfig } from '../config'
import {
  DIAGRAM_RENDER,
  DiagramRenderError,
  diagramFitsFloor,
  renderDiagramToFile,
  serializeDiagram,
} from './render'
import { CAR_VIEWBOX } from './regions'

function configWithFloor(minWidth: number, minHeight: number): MediaConfig {
  return {
    clarity: { minWidth, minHeight, blurVarianceThreshold: 100, blurAnalysisMaxEdge: 512 },
    maxFileMb: 15,
    buckets: [{ bucket: 'damage_diagram', allowUpload: false, contentTypes: ['image/png'] }],
  }
}

describe('the diagram export', () => {
  it('renders above the resolution floor, in the drawing’s own aspect ratio', () => {
    // `damage_diagram` is an image bucket, so §7.2 item 1's floor is applied to this PNG by the
    // server exactly as to a photograph. The SVG's natural 400x300 would be refused every time.
    expect(DIAGRAM_RENDER.width / DIAGRAM_RENDER.height).toBeCloseTo(
      CAR_VIEWBOX.width / CAR_VIEWBOX.height,
    )
    expect(diagramFitsFloor(configWithFloor(1024, 768))).toBe(true)
  })

  it('refuses to draw when the floor has been raised past the render size', () => {
    // The runtime half of the guard. The other half is `TheClarityFloorAdmitsADamageDiagram` in the
    // xUnit suite, which is the only place that can read the *configured* value — a jsdom test
    // cannot see appsettings.Placeholders.json, so this one can only prove the predicate works.
    expect(diagramFitsFloor(configWithFloor(2000, 1500))).toBe(false)
    expect(diagramFitsFloor(configWithFloor(1600, 1201))).toBe(false)
    expect(diagramFitsFloor(configWithFloor(1600, 1200))).toBe(true)
  })

  it('assumes a fit while the config is still in flight', () => {
    // Answering "no" here would show a misconfiguration alert for a request that has simply not
    // landed — 2.5's `configLoaded` / `ready` lesson, one rung further down.
    expect(diagramFitsFloor(undefined)).toBe(true)
  })

  it('stamps the export size onto the serialized markup', () => {
    // A viewBox alone leaves an SVG's intrinsic size undefined, and some engines rasterize such an
    // image to nothing at all — a blank diagram that still passes every dimension check.
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
    svg.setAttribute('viewBox', '0 0 400 300')
    svg.setAttribute('width', '400')

    const markup = serializeDiagram(svg)

    expect(markup).toContain(`width="${DIAGRAM_RENDER.width}"`)
    expect(markup).toContain(`height="${DIAGRAM_RENDER.height}"`)
    expect(markup).toContain('viewBox="0 0 400 300"')
  })

  it('does not disturb the element on screen while serializing it', () => {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
    svg.setAttribute('width', '400')

    serializeDiagram(svg)

    expect(svg.getAttribute('width')).toBe('400')
  })

  it('produces a PNG File at the export size', async () => {
    const render = vi.fn(() => Promise.resolve(new Blob([new Uint8Array([1, 2, 3])])))

    const file = await renderDiagramToFile('<svg/>', 'abcd1234', render)

    expect(render).toHaveBeenCalledWith('<svg/>', DIAGRAM_RENDER)
    expect(file.name).toBe('damage-diagram-abcd1234.png')
    expect(file.type).toBe('image/png')
  })

  it('surfaces a render failure rather than sending nothing', async () => {
    const render = () => Promise.reject(new DiagramRenderError('failed'))

    await expect(renderDiagramToFile('<svg/>', 'abcd1234', render)).rejects.toBeInstanceOf(
      DiagramRenderError,
    )
  })
})
