/**
 * design.md §5.3's five mandatory car sides, bound to the §7.1 buckets they are filed under
 * (slice 6.1).
 *
 * **This binding lives here rather than in `media/carshots/` on purpose.** The selector draws a car
 * and reports which side was tapped; which bucket that side's photograph belongs in is a §7.1 rule
 * about *this* flow, and `media/` has to stay bucket-agnostic to remain reusable — the property
 * `reusability.test.ts` enforces. The same selector could serve a garage flow later without knowing
 * any of these names.
 *
 * **Five buckets rather than one bucket and a side field**: the server has no side slot —
 * `MediaUploadTarget` carries owner, visa and actor, and the multipart contract reads `bucket` and
 * `origin` and nothing else — so the side *is* the bucket. It also keeps `CapturePanel`'s
 * `${bucket}-capture` DOM id unique, which matters because P1 shows one panel per selected side and a
 * duplicate id would bind the label to the wrong input.
 *
 * The order is §5.3's and matches `MediaBuckets.PublicCarShots` on the server, so "N of 5" and the
 * checklist read the same way round on both sides of the wire.
 */
export interface CarShotPanel {
  /** Matches a `CAR_SHOT_ZONES` id, which is how a tap on the car finds its bucket. */
  id: string
  bucket: string
  /** The side, as the checklist and the capture panel's heading name it. */
  label: string
  /** Finishes the capture control's accessible name: "Take a photo — the front of the car". */
  qualifier: string
}

export const CAR_SHOT_PANELS: readonly CarShotPanel[] = [
  { id: 'front', bucket: 'public_car_front', label: 'Front', qualifier: 'the front of the car' },
  { id: 'rear', bucket: 'public_car_rear', label: 'Rear', qualifier: 'the rear of the car' },
  { id: 'left', bucket: 'public_car_left', label: 'Left', qualifier: 'the left side of the car' },
  { id: 'right', bucket: 'public_car_right', label: 'Right', qualifier: 'the right side of the car' },
  { id: 'roof', bucket: 'public_car_roof', label: 'Roof', qualifier: 'the roof of the car' },
]

/** The five bucket names, for splitting a document list into photographs and supporting documents. */
export const CAR_SHOT_BUCKETS: readonly string[] = CAR_SHOT_PANELS.map((panel) => panel.bucket)

export function carShotPanel(id: string): CarShotPanel {
  const panel = CAR_SHOT_PANELS.find((candidate) => candidate.id === id)
  // Unreachable through the UI — the selector only reports ids it was given — but a wrong id here
  // would upload a photograph to whichever bucket happened to be first, which is a car side filed
  // under the wrong name in AXA's own quotation. Fail loudly instead.
  if (!panel) throw new Error(`No car shot panel '${id}'`)
  return panel
}
