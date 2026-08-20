import { describe, expect, it } from 'vitest'
import { clearMarks, isMarked, markLabels, NO_MARKS, toggleMark } from './marks'
import { CAR_REGIONS } from './regions'

describe('damage marks', () => {
  it('marks a panel and unmarks it on a second tap', () => {
    const once = toggleMark(NO_MARKS, 'bonnet')
    expect(isMarked(once, 'bonnet')).toBe(true)

    const twice = toggleMark(once, 'bonnet')
    expect(isMarked(twice, 'bonnet')).toBe(false)
    expect(twice).toHaveLength(0)
  })

  it('keeps several marks in the order they were tapped', () => {
    // The order is the order the expert looked at the car, and it survives into the caption the
    // claims officer reads in NEXT3.
    const marks = ['rear_bumper', 'bonnet', 'front_left_wing'].reduce(toggleMark, NO_MARKS)

    expect(markLabels(marks)).toEqual(['Rear bumper', 'Bonnet', 'Front left wing'])
  })

  it('ignores an id that names no panel', () => {
    // A mark that names nothing would caption the exported PNG with a blank, which reads as damage
    // to a part nobody can identify.
    const marks = toggleMark(NO_MARKS, 'PLACEHOLDER-not-a-panel')

    expect(marks).toHaveLength(0)
    expect(markLabels(marks)).toEqual([])
  })

  it('clears every mark', () => {
    const marks = ['bonnet', 'roof'].reduce(toggleMark, NO_MARKS)

    expect(clearMarks()).toHaveLength(0)
    expect(marks).toHaveLength(2)   // and the original is untouched — the state is immutable
  })

  it('has a panel for every side of the car', () => {
    // Guards against a region being dropped in an edit: slice 6.1 reuses these as the Option 2 side
    // selector, where §5.3 makes all five shots mandatory.
    const ids = CAR_REGIONS.map((region) => region.id)

    expect(new Set(ids).size).toBe(ids.length)
    for (const required of ['front_bumper', 'rear_bumper', 'roof', 'front_left_door', 'front_right_door']) {
      expect(ids).toContain(required)
    }
  })
})
