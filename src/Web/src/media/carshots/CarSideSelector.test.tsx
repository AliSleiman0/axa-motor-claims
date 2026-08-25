import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { CarSideSelector, type CarShotSide } from './CarSideSelector'
import { CAR_SHOT_ZONES } from './sides'

/**
 * design.md §5.3's "side selected at capture" (slice 6.1).
 *
 * The component is deliberately bucket-agnostic — it is told what the sides are called and reports
 * ids — so these tests say nothing about `public_car_front` either. That separation is what
 * `reusability.test.ts` enforces, and testing it in the same terms is how it stays true.
 */
const SIDES: CarShotSide[] = [
  { id: 'front', label: 'Front', done: false },
  { id: 'rear', label: 'Rear', done: false },
  { id: 'left', label: 'Left', done: false },
  { id: 'right', label: 'Right', done: false },
  { id: 'roof', label: 'Roof', done: false },
]

describe('CarSideSelector', () => {
  it('offers the five sides as a radio group, exactly one chosen', () => {
    render(<CarSideSelector sides={SIDES} selected="front" onSelect={() => {}} />)

    // A radio group rather than the diagram's checkboxes: the customer is choosing which photograph
    // they are about to take, not recording several facts at once.
    expect(screen.getByRole('radiogroup')).toBeDefined()

    const sides = screen.getAllByRole('radio')
    expect(sides.map((side) => side.getAttribute('aria-label'))).toEqual([
      'Front',
      'Rear',
      'Left',
      'Right',
      'Roof',
    ])
    expect(sides.filter((side) => side.getAttribute('aria-checked') === 'true')).toHaveLength(1)
  })

  it('reports a tap by id and does not decide anything itself', async () => {
    const onSelect = vi.fn()
    render(<CarSideSelector sides={SIDES} selected="front" onSelect={onSelect} />)

    await userEvent.click(screen.getByRole('radio', { name: 'Roof' }))

    expect(onSelect).toHaveBeenCalledWith('roof')
    // Controlled: the caller owns the selection, so nothing moved on its own here.
    expect(screen.getByRole('radio', { name: 'Front' }).getAttribute('aria-checked')).toBe('true')
  })

  it('answers the keyboard as well as a thumb', async () => {
    const onSelect = vi.fn()
    render(<CarSideSelector sides={SIDES} selected="front" onSelect={onSelect} />)

    screen.getByRole('radio', { name: 'Left' }).focus()
    await userEvent.keyboard('{Enter}')

    expect(onSelect).toHaveBeenCalledWith('left')
  })

  it('says which sides are done, in the accessible name and in words', () => {
    const sides = SIDES.map((side) => ({ ...side, done: side.id === 'front' || side.id === 'roof' }))
    render(<CarSideSelector sides={sides} selected="rear" onSelect={() => {}} />)

    // The fill is the visible feedback and jsdom applies no stylesheet, so what a test here can
    // honestly check is the name a screen reader gets and the checklist beside the car — the version
    // that also survives a colour-blind reader and a phone in sunlight.
    expect(screen.getByRole('radio', { name: 'Front — photographed' })).toBeDefined()
    expect(screen.getByRole('radio', { name: 'Rear' })).toBeDefined()

    expect(screen.getAllByText('Taken')).toHaveLength(2)
    expect(screen.getAllByText('Still needed')).toHaveLength(3)
  })

  it('draws every panel of the car exactly once', () => {
    // The five zones partition all fifteen `CAR_REGIONS`: no part of the drawing is dead to the touch,
    // and none is drawn twice into a seam. Asserted on the data rather than the DOM, because it is a
    // property of `sides.ts` that a later edit to one zone could silently break.
    const covered = CAR_SHOT_ZONES.flatMap((zone) => zone.regions.map((region) => region.id))

    expect(new Set(covered).size).toBe(covered.length)
    expect(covered).toHaveLength(15)
  })

  it('turns the label along a zone that is taller than it is wide', () => {
    // **The browser pass found this and no jsdom test could have.** Drawn horizontally, "Right"
    // measured 38 user units against a 30-unit door strip — four over the edge on each side — and
    // "Left" landed exactly on the seam between the two door rects, struck through by its own
    // outline. The artboard rotates these two for the same reason.
    //
    // Asserted on the transform rather than on geometry, because jsdom lays nothing out: what a test
    // here can honestly check is that the narrow zones ask to be rotated and the wide ones do not.
    // The pixels were the browser pass's job, and it measured all five inside their zones afterwards.
    const { container } = render(
      <CarSideSelector sides={SIDES} selected="front" onSelect={() => {}} />,
    )

    const rotated = (label: string) =>
      [...container.querySelectorAll('text')]
        .find((t) => t.textContent === label)
        ?.getAttribute('transform')

    expect(rotated('Left')).toMatch(/^rotate\(-90 /)
    expect(rotated('Right')).toMatch(/^rotate\(-90 /)

    for (const wide of ['Front', 'Rear', 'Roof']) {
      expect(rotated(wide), `${wide} is wider than it is tall and should read straight`).toBeNull()
    }
  })

  it('ignores a side it has no zone for rather than drawing a hole', () => {
    render(
      <CarSideSelector
        sides={[...SIDES, { id: 'boot-lid', label: 'Boot lid', done: false }]}
        selected="front"
        onSelect={() => {}}
      />,
    )

    expect(screen.getAllByRole('radio')).toHaveLength(5)
  })
})
