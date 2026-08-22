import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import { CapturePanel } from './CapturePanel'
import type { MediaConfig } from './config'

const PATH = '/api/expert/assignments/00000000-0000-0000-0000-00000000a11c/documents'

const CONFIG: MediaConfig = {
  clarity: { minWidth: 1024, minHeight: 768, blurVarianceThreshold: 100, blurAnalysisMaxEdge: 512 },
  maxFileMb: 15,
  buckets: [
    { bucket: 'insured_car_photo', allowUpload: false, contentTypes: ['image/jpeg', 'image/png'] },
    { bucket: 'tp_car_photo', allowUpload: false, contentTypes: ['image/jpeg', 'image/png'] },
    {
      bucket: 'insured_documents',
      allowUpload: true,
      contentTypes: ['image/jpeg', 'image/png', 'application/pdf'],
    },
  ],
}

/**
 * Per-bucket accessible names (slice 4.4, pass-1 decision 2 — the week-6 a11y fix pulled forward).
 *
 * E2 renders five of these panels and every capture control on it was called "Take a photo", so a
 * screen reader announced five identical buttons and the expert had to count panels to know which
 * car they were photographing.
 */
describe('CapturePanel — accessible names', () => {
  it('qualifies the camera control with what the bucket holds', () => {
    show('insured_car_photo', 'insured car')

    expect(screen.getByLabelText('Take a photo — insured car')).toBeDefined()
  })

  it('qualifies the file control too', () => {
    show('insured_documents', 'insured document')

    expect(screen.getByLabelText('Choose a file — insured document')).toBeDefined()
  })

  it('keeps the visible label as the shipped string', () => {
    // The words the canvas draws and the demo script names are unchanged: pass 1 puts the qualifier
    // in the *accessible* name only, and a visible label that is a prefix of the accessible name is
    // exactly what WCAG 2.5.3 asks for. Both lookups therefore find the same control.
    show('insured_car_photo', 'insured car')

    const byVisibleLabel = screen.getByLabelText('Take a photo')
    const byAccessibleName = screen.getByLabelText('Take a photo — insured car')
    expect(byVisibleLabel).toBe(byAccessibleName)
  })

  it('tells two panels on one screen apart', () => {
    // The whole point. Without the qualifier these two controls are indistinguishable, and this is
    // the *shape* of E2 — five panels, five identical buttons.
    render(
      <TestQueryProvider>
        <CapturePanel
          path={PATH}
          bucket="insured_car_photo"
          label="Insured car photos"
          config={CONFIG}
          qualifier="insured car"
        />
        <CapturePanel
          path={PATH}
          bucket="tp_car_photo"
          label="Third-party car photos"
          config={CONFIG}
          qualifier="third-party car"
        />
      </TestQueryProvider>,
    )

    expect(screen.getByLabelText('Take a photo — insured car')).toBeDefined()
    expect(screen.getByLabelText('Take a photo — third-party car')).toBeDefined()
  })

  it('falls back to the bare action when a caller supplies no qualifier', () => {
    // A screen with one bucket on it need not invent one, and every pre-4.4 call site is unchanged.
    show('insured_car_photo', undefined)

    expect(screen.getByLabelText('Take a photo')).toBeDefined()
    expect(screen.queryByLabelText(/Take a photo — /)).toBeNull()
  })
})

function show(bucket: string, qualifier: string | undefined) {
  return render(
    <TestQueryProvider>
      <CapturePanel
        path={PATH}
        bucket={bucket}
        label="PLACEHOLDER bucket"
        config={CONFIG}
        qualifier={qualifier}
      />
    </TestQueryProvider>,
  )
}
