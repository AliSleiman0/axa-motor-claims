import type { Ref } from 'react'
import { isMarked, type Marks } from './marks'
import { CAR_REGIONS, CAR_VIEWBOX } from './regions'

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
          <rect
            key={region.id}
            x={region.x}
            y={region.y}
            width={region.width}
            height={region.height}
            // A plain marker red, not a brand colour: design.md §7's deliverable list excludes
            // branding and colour, and AXA supplied none.
            fill={marked ? '#cc0000' : '#f2f2f2'}
            stroke="#333333"
            strokeWidth={1.5}
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
        )
      })}
    </svg>
  )
}
