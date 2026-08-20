import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ClarityConfirm } from './ClarityConfirm'
import type { ClarityVerdict } from './clarity'

const VERDICT: ClarityVerdict = {
  ok: true,
  failure: null,
  width: 1600,
  height: 1200,
  variance: 19312,
}

function show(file: File, verdict: ClarityVerdict | null) {
  return render(
    <ClarityConfirm
      previewUrl="blob:PLACEHOLDER"
      file={file}
      verdict={verdict}
      pending={false}
      onConfirm={vi.fn()}
      onRetake={vi.fn()}
    />,
  )
}

function imageFile() {
  return new File([new Uint8Array([1])], 'PLACEHOLDER-photo.jpg', { type: 'image/jpeg' })
}

function pdfFile() {
  return new File([new Uint8Array([1])], 'PLACEHOLDER-report.pdf', { type: 'application/pdf' })
}

describe('ClarityConfirm (E4)', () => {
  it('shows the photo full-bleed with its verdict', () => {
    // §7.2 item 3: the expert is deciding whether this photo is good enough for a claims assessor.
    const { container } = show(imageFile(), VERDICT)

    const img = container.querySelector('img.capture-preview')
    expect(img).not.toBeNull()
    expect(img?.getAttribute('src')).toBe('blob:PLACEHOLDER')
    expect(screen.getByText('1600×1200, sharpness 19312')).toBeDefined()
    expect(screen.getByRole('button', { name: 'Retake' })).toBeDefined()
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeDefined()
  })

  it('names a PDF instead of rendering it as a broken image', () => {
    // Found in the browser, not by the suite: a PDF in an <img> renders a broken-image icon on a
    // black bar, labelled "the photo about to be sent to AXA". That asks the expert to confirm a
    // document they cannot see, and miscalls it a photo.
    const { container } = show(pdfFile(), null)

    expect(container.querySelector('img')).toBeNull()
    expect(screen.getByText('PLACEHOLDER-report.pdf')).toBeDefined()
    expect(screen.queryByText(/sharpness/)).toBeNull()
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeDefined()
  })
})
