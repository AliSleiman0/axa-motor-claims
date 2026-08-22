import { describe, expect, it, vi } from 'vitest'
import { APPROVAL_RENDER, buildApprovalSvg, formatStamp, renderApprovalToFile, wrapText } from './approval'

const DECISION = {
  declarationId: '00000000-0000-0000-0000-0000000d0001',
  plateNo: 'PLC-TEST-D1',
  visaNo: 'PLACEHOLDER-VISA-0001',
  officerName: 'PLACEHOLDER Officer',
  preparedAt: new Date('2026-08-22T01:30:00Z'),
  comments: 'Approved. Proceed with the repair.',
}

describe('wrapText', () => {
  it('wraps on words rather than mid-word', () => {
    const lines = wrapText('the quick brown fox jumps over the lazy dog', 15)

    expect(lines).toEqual(['the quick brown', 'fox jumps over', 'the lazy dog'])
    // Nothing lost: SVG <text> does not wrap, so anything this function drops runs off the edge of
    // the PNG and is gone with nothing reporting it.
    expect(lines.join(' ')).toBe('the quick brown fox jumps over the lazy dog')
  })

  it('honours the officer’s own line breaks', () => {
    // A list of conditions silently reflowed into one paragraph changes what the decision appears
    // to say, on an artifact that ends up in AXA's claim folder.
    expect(wrapText('one\ntwo\nthree', 40)).toEqual(['one', 'two', 'three'])
  })

  it('chops a single word longer than the line instead of overflowing', () => {
    const lines = wrapText('short aaaaaaaaaaaaaaaaaaaa end', 10)

    expect(lines.every((line) => line.length <= 10)).toBe(true)
    expect(lines.join('')).toContain('aaaaaaaaaaaaaaaaaaaa')
  })
})

describe('buildApprovalSvg', () => {
  it('carries every field of the decision', () => {
    const svg = buildApprovalSvg(DECISION)

    expect(svg).toContain('APPROVED')
    expect(svg).toContain(DECISION.visaNo)
    expect(svg).toContain(DECISION.plateNo)
    expect(svg).toContain(DECISION.declarationId)
    expect(svg).toContain(DECISION.officerName)
    expect(svg).toContain('Approved. Proceed with the repair.')
  })

  it('stamps the time in UTC with the zone written out', () => {
    // 2.4's lesson: a bare timestamp reads as local, and a four-hour error in GST is invisible.
    expect(formatStamp(DECISION.preparedAt)).toBe('2026-08-22 01:30:00 UTC')
    expect(buildApprovalSvg(DECISION)).toContain('2026-08-22 01:30:00 UTC')
  })

  it('escapes markup in the officer’s free text', () => {
    // Not cosmetic. An unescaped `<` produces markup the rasterizer refuses, so an approval reading
    // "damage < 500 AED" would fail to render at all and the officer would be told the image could
    // not be prepared, with no clue why.
    const svg = buildApprovalSvg({ ...DECISION, comments: 'damage < 500 AED & </text> "ok"' })

    expect(svg).toContain('&lt;')
    expect(svg).toContain('&amp;')
    expect(svg).not.toContain('</text> "ok"')
    // Exactly the tags this builder wrote, and none the comment smuggled in.
    expect(svg.split('</text>').length - 1).toBe(svg.split('<text').length - 1)
  })

  it('paints an opaque background and uses no foreignObject', () => {
    const svg = buildApprovalSvg(DECISION)

    // A transparent PNG passes every dimension check the server applies and is still unreadable.
    expect(svg).toContain('fill="#ffffff"')
    // A serialized SVG carries no page CSS, and foreignObject taints the canvas — toBlob then fails
    // on some engines and nowhere else.
    expect(svg).not.toContain('foreignObject')
    expect(svg).not.toContain('class=')
  })

  it('renders at the size the server floor expects', () => {
    const svg = buildApprovalSvg(DECISION)

    expect(APPROVAL_RENDER).toEqual({ width: 1600, height: 1200 })
    expect(svg).toContain(`width="${APPROVAL_RENDER.width}"`)
    expect(svg).toContain(`height="${APPROVAL_RENDER.height}"`)
  })

  it('says so rather than silently dropping an enormous comment', () => {
    const svg = buildApprovalSvg({ ...DECISION, comments: 'word '.repeat(4000) })

    expect(svg).toContain('continued in the claim officer notes')
  })

  it('says "(none)" rather than leaving the section blank', () => {
    expect(buildApprovalSvg({ ...DECISION, comments: '   ' })).toContain('(none)')
  })
})

describe('renderApprovalToFile', () => {
  it('names the file after the declaration and hands the renderer the standard size', async () => {
    const render = vi.fn(() => Promise.resolve(new Blob([new Uint8Array([1, 2, 3])])))

    const file = await renderApprovalToFile(DECISION, render)

    expect(file.name).toBe(`approval-${DECISION.declarationId}.png`)
    expect(file.type).toBe('image/png')
    expect(render).toHaveBeenCalledWith(expect.stringContaining('<svg'), APPROVAL_RENDER)
  })
})
