/**
 * design.md §1's simplification of the BRD's *"a car body diagram where the expert can mark the
 * accident spot"*: **a static SVG car, tap-to-mark hotspots, flattened to PNG via canvas**.
 *
 * Deliberately plain. §1 dropped diagram polish to fund Broker Option 2 (§11), so this is a
 * top-down car drawn from rectangles — legible on a phone at a roadside, and nothing more. Making
 * it pretty is spending money the project does not have.
 *
 * The geometry lives here as data because two things draw from it: the interactive component and,
 * through it, the PNG that reaches NEXT3. Slice 6.1 reuses the same shapes as the Option 2 side
 * selector, which is why the part names are generic vehicle vocabulary and not expert-flow labels.
 */

/** The SVG coordinate space. 4:3, matching the export size so nothing is letterboxed. */
export const CAR_VIEWBOX = { width: 400, height: 300 } as const

export interface CarRegion {
  id: string
  /** Shown to the expert, and the accessible name of the hotspot. */
  label: string
  x: number
  y: number
  width: number
  height: number
}

/**
 * Fifteen panels of a car seen from above: nose at the top, tail at the bottom, driver's side on
 * the left of the drawing. Left and right are the *car's* sides as drawn, which is how a person
 * standing at the front of the vehicle reads it.
 */
export const CAR_REGIONS: readonly CarRegion[] = [
  { id: 'front_bumper', label: 'Front bumper', x: 110, y: 20, width: 180, height: 25 },

  { id: 'front_left_wing', label: 'Front left wing', x: 110, y: 45, width: 30, height: 45 },
  { id: 'bonnet', label: 'Bonnet', x: 140, y: 45, width: 120, height: 45 },
  { id: 'front_right_wing', label: 'Front right wing', x: 260, y: 45, width: 30, height: 45 },

  { id: 'front_left_door', label: 'Front left door', x: 110, y: 90, width: 30, height: 60 },
  { id: 'windscreen', label: 'Windscreen', x: 140, y: 90, width: 120, height: 20 },
  { id: 'front_right_door', label: 'Front right door', x: 260, y: 90, width: 30, height: 60 },

  { id: 'roof', label: 'Roof', x: 140, y: 110, width: 120, height: 80 },

  { id: 'rear_left_door', label: 'Rear left door', x: 110, y: 150, width: 30, height: 60 },
  { id: 'rear_right_door', label: 'Rear right door', x: 260, y: 150, width: 30, height: 60 },

  { id: 'rear_window', label: 'Rear window', x: 140, y: 190, width: 120, height: 20 },

  { id: 'rear_left_wing', label: 'Rear left wing', x: 110, y: 210, width: 30, height: 45 },
  { id: 'boot', label: 'Boot', x: 140, y: 210, width: 120, height: 45 },
  { id: 'rear_right_wing', label: 'Rear right wing', x: 260, y: 210, width: 30, height: 45 },

  { id: 'rear_bumper', label: 'Rear bumper', x: 110, y: 255, width: 180, height: 25 },
]

export function findRegion(id: string): CarRegion | undefined {
  return CAR_REGIONS.find((region) => region.id === id)
}
