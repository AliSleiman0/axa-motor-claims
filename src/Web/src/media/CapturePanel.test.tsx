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
    {
      bucket: 'insured_documents',
      allowUpload: true,
      contentTypes: ['image/jpeg', 'image/png', 'application/pdf'],
    },
  ],
}

function show(bucket: string, config: MediaConfig = CONFIG) {
  return render(
    <TestQueryProvider>
      <CapturePanel path={PATH} bucket={bucket} label="PLACEHOLDER bucket" config={config} />
    </TestQueryProvider>,
  )
}

describe('CapturePanel', () => {
  it('offers no file picker at all for a capture-only bucket', () => {
    // §7.1's capture-only rule expressed as what exists on screen. The hook refuses a picked file
    // and the server refuses it again, but the control an expert cannot reach is the real guard.
    show('insured_car_photo')

    expect(screen.getByLabelText('Take a photo')).toBeDefined()
    expect(screen.queryByLabelText('Choose a file')).toBeNull()
  })

  it('offers both controls for a bucket that allows upload', () => {
    show('insured_documents')

    expect(screen.getByLabelText('Take a photo')).toBeDefined()
    expect(screen.getByLabelText('Choose a file')).toBeDefined()
  })

  it('asks the camera for the rear-facing capture', () => {
    show('insured_car_photo')

    const input = screen.getByLabelText('Take a photo')
    expect(input.getAttribute('capture')).toBe('environment')
  })

  it('offers only the content types its bucket accepts', () => {
    // An image bucket must not advertise PDFs: the server answers 415 for one, and that is a
    // rejection the expert cannot act on at the roadside.
    show('insured_car_photo')
    expect(screen.getByLabelText('Take a photo').getAttribute('accept')).toBe('image/jpeg,image/png')

    show('insured_documents')
    const inputs = screen.getAllByLabelText('Take a photo')
    expect(inputs[inputs.length - 1].getAttribute('accept')).toBe(
      'image/jpeg,image/png,application/pdf',
    )
  })

  it('shows the already-captured count for the bucket', () => {
    render(
      <TestQueryProvider>
        <CapturePanel
          path={PATH}
          bucket="insured_car_photo"
          label="Insured car photos"
          config={CONFIG}
          count={3}
        />
      </TestQueryProvider>,
    )

    expect(screen.getByText('Insured car photos (3)')).toBeDefined()
  })

  it('captures nothing until the settings have loaded', () => {
    // Without thresholds there is no gate, and a bucket rendered before them would be a capture
    // path with §7.2 silently switched off. Rendered inline rather than through `show`, because
    // passing `undefined` to a defaulted parameter would quietly hand back the default.
    render(
      <TestQueryProvider>
        <CapturePanel
          path={PATH}
          bucket="insured_car_photo"
          label="PLACEHOLDER bucket"
          config={undefined}
        />
      </TestQueryProvider>,
    )

    expect(screen.getByText('Loading photo settings…')).toBeDefined()
    expect(screen.queryByLabelText('Take a photo')).toBeNull()
  })

  it('says a bucket is unconfigured rather than leaving it loading for ever', () => {
    // The two undefined-rule cases must not be collapsed. With the config loaded and the bucket
    // absent, "loading" would never resolve and would point the next developer at the network tab
    // instead of at the missing §7.1 registry entry — exactly what slice 5.1 will hit when a garage
    // bucket reaches a screen before its MediaBuckets entry and migration.
    show('garage_car_photo')

    expect(screen.queryByText('Loading photo settings…')).toBeNull()
    expect(screen.getByRole('alert').textContent).toContain('not configured')
    expect(screen.queryByLabelText('Take a photo')).toBeNull()
  })
})
