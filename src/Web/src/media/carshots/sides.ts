import { CAR_REGIONS, CAR_VIEWBOX, type CarRegion } from '../diagram/regions'

/**
 * The five sides of a car, as areas of the same drawing the expert marks damage on
 * (design.md §5.3's "side selection", slice 6.1).
 *
 * `regions.ts` reserved itself for this — *"slice 6.1 reuses the same shapes as the Option 2 side
 * selector, which is why the part names are generic vehicle vocabulary and not expert-flow labels"* —
 * so there is one car in this application, drawn once, used two ways: fifteen panels to mark damage
 * on, and five zones to photograph.
 *
 * **Geometry and nothing else.** No bucket name appears in this folder and no upload path is known to
 * it, because §7.1's buckets are server-side rules and `media/` has to stay reusable by any caller —
 * the same property `reusability.test.ts` enforces for the rest of the module. The binding from a
 * zone id to the bucket its photograph is filed under lives with the page that owns the upload.
 */
export interface CarShotZone {
  /** Stable id, used as the radio value and as the key a caller binds its own meaning to. */
  id: string
  /** Shown on the car and used as the accessible name of the hotspot. */
  label: string
  /** The `CAR_REGIONS` panels this zone covers — the drawing, not a separate set of rectangles. */
  regions: readonly CarRegion[]
}

function regions(...ids: readonly string[]): readonly CarRegion[] {
  return ids.map((id) => {
    const region = CAR_REGIONS.find((candidate) => candidate.id === id)
    // A typo here would silently produce a zone with a hole in it — a side of the car that cannot be
    // tapped, on the one screen whose reader has never seen the app before and has no way to report
    // it. Cheaper to fail at module load.
    if (!region) throw new Error(`No car region '${id}'`)
    return region
  })
}

/**
 * The five zones, **in the order §5.3 lists them** — front, rear, left, right, roof. The order is the
 * reading order of the checklist beneath the car, so it is written once here rather than in the page.
 *
 * Together they partition all fifteen panels: every part of the drawing belongs to exactly one side,
 * so there is no dead area a customer can tap and no panel drawn twice.
 */
export const CAR_SHOT_ZONES: readonly CarShotZone[] = [
  {
    id: 'front',
    label: 'Front',
    regions: regions('front_bumper', 'front_left_wing', 'bonnet', 'front_right_wing'),
  },
  {
    id: 'rear',
    label: 'Rear',
    regions: regions('rear_left_wing', 'boot', 'rear_right_wing', 'rear_bumper'),
  },
  {
    id: 'left',
    label: 'Left',
    regions: regions('front_left_door', 'rear_left_door'),
  },
  {
    id: 'right',
    label: 'Right',
    regions: regions('front_right_door', 'rear_right_door'),
  },
  {
    id: 'roof',
    label: 'Roof',
    regions: regions('windscreen', 'roof', 'rear_window'),
  },
]

export { CAR_VIEWBOX }
