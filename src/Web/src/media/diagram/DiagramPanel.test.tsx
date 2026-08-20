import { render, screen, waitFor } from '@testing-library/react'
import { fireEvent } from '@testing-library/dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { TestQueryProvider } from '../../testing/TestQueryProvider'
import type { MediaConfig } from '../config'
import { DiagramPanel } from './DiagramPanel'

const PATH = '/api/expert/assignments/00000000-0000-0000-0000-00000000a11c/documents'

function config(minWidth = 1024, minHeight = 768): MediaConfig {
  return {
    clarity: { minWidth, minHeight, blurVarianceThreshold: 100, blurAnalysisMaxEdge: 512 },
    maxFileMb: 15,
    buckets: [{ bucket: 'damage_diagram', allowUpload: false, contentTypes: ['image/png'] }],
  }
}

const renderPng = () => Promise.resolve(new Blob([new Uint8Array([1, 2, 3])]))

describe('DiagramPanel', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn(() =>
      Promise.resolve(new Response(JSON.stringify({ id: 'doc-1' }), { status: 201 })),
    )
    vi.stubGlobal('fetch', fetchMock)
    vi.stubGlobal('URL', { ...URL, createObjectURL: () => 'blob:PLACEHOLDER', revokeObjectURL: vi.fn() })
  })

  it('marks a panel and captions it', () => {
    show()

    fireEvent.click(screen.getByRole('checkbox', { name: 'Bonnet' }))

    expect(screen.getByRole('checkbox', { name: 'Bonnet' }).getAttribute('aria-checked')).toBe('true')
    expect(screen.getByText('Marked: Bonnet')).toBeDefined()
  })

  it('takes the diagram to the confirm screen with no sharpness verdict', async () => {
    // A canvas export has no focus to measure. Showing a sharpness figure for one would be a number
    // with no meaning on the screen whose whole purpose is that a person judges the file.
    show()

    fireEvent.click(screen.getByRole('checkbox', { name: 'Roof' }))
    fireEvent.click(screen.getByRole('button', { name: 'Use this diagram' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Confirm' })).toBeDefined())
    expect(screen.queryByText(/sharpness/)).toBeNull()
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('uploads only once Confirm is pressed', async () => {
    show()

    fireEvent.click(screen.getByRole('button', { name: 'Use this diagram' }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Confirm' })).toBeDefined())
    fireEvent.click(screen.getByRole('button', { name: 'Confirm' }))

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(url).toBe(PATH)

    const body = init.body as FormData
    expect([...body.keys()]).toEqual(['bucket', 'origin', 'file'])
    expect(body.get('bucket')).toBe('damage_diagram')
    // Produced in the app, never picked from a gallery — and the server refuses `uploaded` here.
    expect(body.get('origin')).toBe('captured')
  })

  it('sends an unmarked diagram if the expert asks it to', async () => {
    // "No damage to any panel" is a statement an expert may want to make. Refusing to send one would
    // be a rule the BRD does not state — smaller interpretation.
    show()

    fireEvent.click(screen.getByRole('button', { name: 'Use this diagram' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Confirm' })).toBeDefined())
  })

  it('refuses loudly when the resolution floor has been raised past the render size', () => {
    // Without this the expert draws a diagram, presses Confirm, and gets `image_too_small` — which
    // blames the drawing for a configuration change they cannot see or fix.
    show({ config: config(2000, 1500) })

    expect(screen.getByRole('alert').textContent).toContain('2000×1500')
    expect(screen.queryByRole('button', { name: 'Use this diagram' })).toBeNull()
    expect(screen.queryByRole('checkbox', { name: 'Bonnet' })).toBeNull()
  })

  it('says the section is unconfigured rather than loading for ever', () => {
    // 2.5's trap: `configLoaded` and `ready` are separate flags. Collapsed, a bucket the server has
    // never heard of shows "Loading…" permanently, with a clean network tab and an empty console.
    show({ config: { ...config(), buckets: [] } })

    expect(screen.getByRole('alert').textContent).toContain('not configured')
    expect(screen.queryByText(/Loading diagram settings/)).toBeNull()
  })

  it('shows loading only while the config is genuinely in flight', () => {
    show({ config: undefined })

    expect(screen.getByText('Loading diagram settings…')).toBeDefined()
  })

  it('explains a render failure instead of failing silently', async () => {
    show({ render: () => Promise.reject(new Error('canvas said no')) })

    fireEvent.click(screen.getByRole('button', { name: 'Use this diagram' }))

    await waitFor(() => expect(screen.getByRole('alert')).toBeDefined())
    expect(screen.getByRole('alert').textContent).toContain('could not be prepared')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  function show(overrides: Partial<Parameters<typeof DiagramPanel>[0]> = {}) {
    return render(
      <TestQueryProvider>
        <DiagramPanel
          path={PATH}
          bucket="damage_diagram"
          label="Damage diagram"
          config={config()}
          render={renderPng}
          makeId={() => 'abcd1234'}
          {...overrides}
        />
      </TestQueryProvider>,
    )
  }
})
