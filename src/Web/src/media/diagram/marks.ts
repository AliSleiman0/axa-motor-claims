import { CAR_REGIONS, findRegion } from './regions'

/**
 * Which panels the expert has marked, as pure state.
 *
 * Ordered, and the order is the tap order: the first thing marked is the first thing the expert
 * looked at, and an ordered list survives into the exported PNG's legend as-is. A Set would be
 * tidier and would throw that away.
 */
export type Marks = readonly string[]

export const NO_MARKS: Marks = []

export function isMarked(marks: Marks, id: string): boolean {
  return marks.includes(id)
}

/**
 * Tap to mark, tap again to unmark. An unknown id is ignored rather than added: the ids come from
 * `CAR_REGIONS`, and a mark that names no panel would export a legend entry pointing at nothing.
 */
export function toggleMark(marks: Marks, id: string): Marks {
  if (!findRegion(id)) return marks
  return isMarked(marks, id) ? marks.filter((mark) => mark !== id) : [...marks, id]
}

export function clearMarks(): Marks {
  return NO_MARKS
}

/**
 * The marked panels' labels, in tap order — what the PNG is captioned with, so the claims officer
 * reading it in NEXT3 gets words and not only a picture.
 */
export function markLabels(marks: Marks): string[] {
  return marks
    .map((id) => CAR_REGIONS.find((region) => region.id === id)?.label)
    .filter((label): label is string => label !== undefined)
}
