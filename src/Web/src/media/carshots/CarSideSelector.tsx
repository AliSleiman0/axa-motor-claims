import { CAR_SHOT_ZONES, CAR_VIEWBOX } from './sides'

/** One side, as the caller sees it: an id, what to call it, and whether it has been photographed. */
export interface CarShotSide {
  id: string
  label: string
  done: boolean
}

interface CarSideSelectorProps {
  /** In the order they should be listed. Ids must match `CAR_SHOT_ZONES`; unknown ones are ignored. */
  sides: readonly CarShotSide[]
  selected: string
  onSelect: (id: string) => void
}

/**
 * design.md §5.3's "side selected at capture" (slice 6.1) — the expert's car, used as a **picker**.
 *
 * The interaction is deliberately not the diagram's. `CarDiagram` is a multi-select over fifteen
 * panels (`role="checkbox"`, mark as many as are damaged); this is a single choice among five
 * (`role="radio"` in a `radiogroup`, exactly one selected at a time), because the customer is
 * choosing which photograph they are about to take, not recording anything. Sharing the geometry and
 * not the component is what that difference costs.
 *
 * **Generic sides in, ids out.** It is told what the five are called and which are done, and reports
 * taps by id; it knows nothing about buckets, upload paths or Broker Option 2. That is what keeps
 * `media/` reusable — the property `reusability.test.ts` enforces — and it is why the same selector
 * could serve a garage or an expert flow later without being rewritten.
 *
 * Inline attributes rather than CSS classes, like `car.tsx`: this SVG lives beside one that gets
 * serialized to a PNG, and a rule that holds for one of them and not the other is the kind that gets
 * copied wrongly.
 */
export function CarSideSelector({ sides, selected, onSelect }: CarSideSelectorProps) {
  const zones = CAR_SHOT_ZONES.map((zone) => ({
    zone,
    side: sides.find((candidate) => candidate.id === zone.id),
  })).filter((entry): entry is { zone: (typeof CAR_SHOT_ZONES)[number]; side: CarShotSide } =>
    entry.side !== undefined,
  )

  return (
    <div className="car-sides">
      <svg
        xmlns="http://www.w3.org/2000/svg"
        viewBox={`0 0 ${CAR_VIEWBOX.width} ${CAR_VIEWBOX.height}`}
        role="radiogroup"
        aria-label="Which side of the car are you photographing?"
        style={{ display: 'block', width: '100%', maxWidth: 320, margin: '0 auto', touchAction: 'manipulation' }}
      >
        <rect x="0" y="0" width={CAR_VIEWBOX.width} height={CAR_VIEWBOX.height} fill="#ffffff" />

        {zones.map(({ zone, side }) => {
          const active = zone.id === selected

          // Green once photographed — the P1Capture artboard's done-fill, and the only feedback a
          // shot gives. There is deliberately no auto-advance to the next side: the customer decides
          // what to photograph next, and a form that moves under someone's thumb is worse than one
          // that waits (smaller interpretation, recorded in scope-decisions).
          const fill = side.done ? '#cfe6da' : active ? '#e7eff9' : '#f2f2f2'
          const stroke = side.done ? '#2f6b4f' : active ? '#2e6bb8' : '#333333'

          return (
            <g
              key={zone.id}
              role="radio"
              aria-checked={active}
              aria-label={side.done ? `${side.label} — photographed` : side.label}
              tabIndex={0}
              style={{ cursor: 'pointer' }}
              onClick={() => onSelect(zone.id)}
              onKeyDown={(event) => {
                // The customer is on a phone and taps; the broker checking their own link is on a
                // desk machine and may not be able to. `car.tsx`'s rule, for the same reason.
                if (event.key === 'Enter' || event.key === ' ') {
                  event.preventDefault()
                  onSelect(zone.id)
                }
              }}
            >
              {zone.regions.map((region) => (
                <rect
                  key={region.id}
                  x={region.x}
                  y={region.y}
                  width={region.width}
                  height={region.height}
                  fill={fill}
                  stroke={stroke}
                  strokeWidth={active ? 3 : 1.5}
                />
              ))}
            </g>
          )
        })}

        {zones.map(({ zone, side }) => {
          const bounds = boundsOf(zone.regions)

          // **Along the strip, not across it.** The two door zones are 30 units wide and 120 tall;
          // drawn horizontally, "Right" measured 38 units — four over the edge on each side — and
          // "Left" landed exactly on the seam between the front and rear door rects, struck through
          // by its own outline. Both were found by looking at it, which is the only way either
          // would have been. The artboard rotates these two for the same reason.
          const upright = bounds.height > bounds.width

          return (
            <text
              key={`${zone.id}-label`}
              x={bounds.cx}
              y={bounds.cy}
              textAnchor="middle"
              dominantBaseline="middle"
              fontSize={16}
              fontFamily="system-ui, sans-serif"
              fill="#2b333b"
              aria-hidden="true"
              transform={upright ? `rotate(-90 ${bounds.cx} ${bounds.cy})` : undefined}
              style={{ pointerEvents: 'none' }}
            >
              {side.label}
            </text>
          )
        })}
      </svg>

      {/*
        The checklist under the car. It is not decoration and not a duplicate control: the fills say
        which sides are done at a glance, and this says it in words — which is the version that
        survives a colour-blind reader and a phone in sunlight.
      */}
      <ul className="car-sides__list">
        {sides.map((side) => (
          <li key={side.id} className="car-sides__item">
            <svg width="18" height="18" viewBox="0 0 20 20" aria-hidden="true">
              <circle
                cx="10"
                cy="10"
                r="8"
                fill={side.done ? '#2f6b4f' : '#ffffff'}
                stroke="#2b333b"
                strokeWidth="1.4"
              />
            </svg>
            <span>{side.label}</span>
            <span className="muted">{side.done ? 'Taken' : 'Still needed'}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}

function boundsOf(regions: readonly { x: number; y: number; width: number; height: number }[]) {
  const left = Math.min(...regions.map((region) => region.x))
  const right = Math.max(...regions.map((region) => region.x + region.width))
  const top = Math.min(...regions.map((region) => region.y))
  const bottom = Math.max(...regions.map((region) => region.y + region.height))

  return {
    cx: (left + right) / 2,
    cy: (top + bottom) / 2,
    width: right - left,
    height: bottom - top,
  }
}
