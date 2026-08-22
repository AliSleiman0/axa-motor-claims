import type { Ref, ReactNode } from 'react'
import { isMarked, type Marks } from './marks'
import { CAR_REGIONS, CAR_VIEWBOX, type CarRegion } from './regions'

interface CarDiagramProps {
  marks: Marks
  onToggle: (id: string) => void
  /** The panel holds this so the live element can be serialized for export — one drawing, not two. */
  svgRef?: Ref<SVGSVGElement>
  disabled?: boolean
}

/**
 * The tap-to-mark car (design.md §1's reading of the BRD's body diagram).
 *
 * **What is on screen is what gets exported.** The PNG is produced by serializing this very
 * element rather than by a second drawing routine, so the expert cannot mark one thing and send
 * another — and there is no geometry to keep in step in two places.
 *
 * Styling is inline rather than in a stylesheet for the same reason: a serialized SVG carries its
 * own attributes but not the page's CSS, so anything expressed in a class would vanish from the
 * exported image and the diagram would arrive at AXA blank.
 */
export function CarDiagram({ marks, onToggle, svgRef, disabled }: CarDiagramProps) {
  return (
    <svg
      ref={svgRef}
      xmlns="http://www.w3.org/2000/svg"
      width={CAR_VIEWBOX.width}
      height={CAR_VIEWBOX.height}
      viewBox={`0 0 ${CAR_VIEWBOX.width} ${CAR_VIEWBOX.height}`}
      role="group"
      aria-label="Car body diagram — tap a panel to mark damage"
      style={{ maxWidth: '100%', touchAction: 'manipulation' }}
    >
      {/* An explicit background: a canvas starts transparent, and a transparent PNG flattened
          against black in whatever views it would hide every line in this drawing. */}
      <rect x="0" y="0" width={CAR_VIEWBOX.width} height={CAR_VIEWBOX.height} fill="#ffffff" />

      {CAR_REGIONS.map((region) => {
        const marked = isMarked(marks, region.id)
        return (
          <g key={region.id}>
            <rect
              x={region.x}
              y={region.y}
              width={region.width}
              height={region.height}
              // A plain marker red, not a brand colour: design.md §7's deliverable list excludes
              // branding and colour, and AXA supplied none. **Light enough to read through**
              // (pass-1 decision 1): the panel name is drawn on top, and the previous solid #cc0000
              // hid the name of the one panel anybody cares about — the marked one.
              fill={marked ? '#f7c8c4' : '#f2f2f2'}
              stroke={marked ? '#a33a31' : '#333333'}
              strokeWidth={marked ? 2.5 : 1.5}
              role="checkbox"
              aria-checked={marked}
              aria-label={region.label}
              tabIndex={disabled ? -1 : 0}
              style={{ cursor: disabled ? 'default' : 'pointer' }}
              onClick={() => {
                if (!disabled) onToggle(region.id)
              }}
              onKeyDown={(event) => {
                // A roadside expert taps; a claim officer reviewing on a desktop may not be able to.
                if (disabled) return
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault()
                  onToggle(region.id)
                }
              }}
            />
            {/*
              The panel name, drawn into the drawing (pass-1 decision 1) — so it is in the exported
              PNG too, since the export serializes this very element. Until now the assessor at AXA
              received a car made of rectangles with two of them shaded, and had to infer from the
              geometry which panel was meant. `pointer-events: none` so the label never swallows the
              tap that belongs to the rect underneath it, and `aria-hidden` because the rect already
              carries `region.label` as its accessible name — announcing it twice is noise.

              Inline attributes, never CSS classes: a serialized SVG carries its attributes but not
              the page's stylesheet, so a class-styled label would arrive at AXA invisible.
            */}
            <text
              x={region.x + region.width / 2}
              y={region.y + region.height / 2}
              textAnchor="middle"
              dominantBaseline="middle"
              fontFamily="'IBM Plex Sans', 'Segoe UI', system-ui, sans-serif"
              fontSize={region.width < 60 ? 7 : 9}
              fontWeight={marked ? 600 : 400}
              fill={marked ? '#7a2a23' : '#5f6c79'}
              aria-hidden="true"
              style={{ pointerEvents: 'none', userSelect: 'none' }}
            >
              {wrapLabel(region)}
            </text>
          </g>
        )
      })}
    </svg>
  )
}

/**
 * A panel name over its rectangle, wrapped onto two lines when the rectangle is narrower than it is.
 *
 * `<tspan>` and not `foreignObject`: a serialized SVG rasterized through a canvas **taints** it on a
 * foreignObject, so the export would fail outright (the same trap `media/png.ts` records for the
 * approval image). Two lines is the most any of these fifteen names needs — "Front left wing" is the
 * longest, and the wing and door columns are 30 px wide.
 */
function wrapLabel(region: CarRegion): ReactNode {
  const words = region.label.split(' ')
  const narrow = region.width < 60

  if (!narrow || words.length === 1) return region.label

  // **One word per line** on the narrow columns. Wrapping only the last word was tried first and
  // "Front left" still ran past a 30 px wing, over the border and into the bonnet beside it — legible
  // on screen and untidy in the PNG that reaches AXA. Every word in these fifteen names fits 30 px.
  const middle = (words.length - 1) / 2
  return (
    <>
      {words.map((word, index) => (
        <tspan
          key={word}
          x={region.x + region.width / 2}
          dy={`${index === 0 ? -middle * 1.05 : 1.05}em`}
        >
          {word}
        </tspan>
      ))}
    </>
  )
}
