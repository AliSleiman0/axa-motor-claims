import { STANDARD_RENDER, renderSvgToFile, type SvgRenderer } from '../png'

/**
 * The size the approval image is flattened to. `approval_image` is an **image** bucket server-side,
 * so §7.2 item 1's resolution floor applies to this export exactly as it does to a photograph — see
 * `STANDARD_RENDER`, which the damage diagram shares for the same reason.
 */
export const APPROVAL_RENDER = STANDARD_RENDER

/** What #18's "approval and comments captured as an image" has to say. */
export interface ApprovalDecision {
  declarationId: string
  plateNo: string
  visaNo: string
  officerName: string
  /**
   * When the officer prepared the decision, as an ISO instant.
   *
   * This is the **browser's** clock, stamped before the approve call, which is why the image labels
   * it as prepared-at and renders it in UTC. `declaration.decided_at` and the §9 audit row are the
   * authoritative record; a bare local timestamp here would be 2.4's four-hour GST bug in a new
   * costume, on an artifact that ends up in AXA's claim folder and outlives every screen.
   */
  preparedAt: Date
  comments: string
}

const TITLE = 'AXA MOTOR CLAIM — DECLARATION APPROVAL'

/** Roughly the character count that fits the content width at the body size below. */
const WRAP_COLUMNS = 78

/**
 * Greedy word wrap.
 *
 * SVG `<text>` does not wrap — a long comment would run off the right edge of the PNG and simply be
 * gone, with nothing anywhere reporting it. So the wrapping is done here, as a pure function that can
 * be tested without a canvas.
 *
 * Explicit line breaks in the officer's comment are honoured, because they are how someone writes a
 * list of conditions and a paragraph that silently reflowed into one line would change what the
 * decision appears to say.
 */
export function wrapText(text: string, columns: number = WRAP_COLUMNS): string[] {
  const lines: string[] = []

  for (const paragraph of text.split(/\r?\n/)) {
    const words = paragraph.split(/\s+/).filter((word) => word.length > 0)
    if (words.length === 0) {
      lines.push('')
      continue
    }

    let current = ''
    for (const word of words) {
      if (current.length === 0) {
        current = word
      } else if (current.length + 1 + word.length <= columns) {
        current = `${current} ${word}`
      } else {
        lines.push(current)
        current = word
      }

      // A single word longer than the line — a URL, or a policy reference — is chopped rather than
      // allowed to overflow. Losing the wrap is better than losing the characters off the edge.
      while (current.length > columns) {
        lines.push(current.slice(0, columns))
        current = current.slice(columns)
      }
    }

    if (current.length > 0) lines.push(current)
  }

  return lines
}

/**
 * XML-escapes a value for an SVG text node.
 *
 * Not optional and not cosmetic: the comments are free text an officer typed, and a single `<` or `&`
 * produces markup the rasterizer refuses — so an approval containing "damage < 500 AED" would fail to
 * render at all, and the officer would see "the image could not be prepared" with no clue why.
 */
function escapeXml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

/** The instant as an unambiguous UTC stamp — the zone is written out, never implied. */
export function formatStamp(at: Date): string {
  return `${at.toISOString().slice(0, 19).replace('T', ' ')} UTC`
}

/**
 * The decision as SVG markup (design.md §5.2's approve row, #18).
 *
 * **`<text>` lines only — never `foreignObject`.** A serialized SVG carries its own attributes but
 * not the page's stylesheet, so HTML inside a `foreignObject` would arrive at AXA unstyled; worse, it
 * taints the canvas in several engines, and a tainted canvas fails `toBlob` — which surfaces as "the
 * image could not be prepared" on some machines and nowhere else. `car.tsx` records the same rule for
 * the same reason.
 *
 * Every colour and size is an inline attribute, and the background is an explicit opaque rect: a
 * transparent PNG would still pass every dimension check the server applies and still be unreadable.
 */
export function buildApprovalSvg(decision: ApprovalDecision): string {
  const { width, height } = APPROVAL_RENDER
  const rows: string[] = []

  const line = (y: number, text: string, size: number, weight: string, fill = '#111111') =>
    `<text x="80" y="${y}" font-family="Helvetica, Arial, sans-serif" font-size="${size}" ` +
    `font-weight="${weight}" fill="${fill}">${escapeXml(text)}</text>`

  rows.push(line(110, TITLE, 40, 'bold'));
  rows.push(
    `<line x1="80" y1="140" x2="${width - 80}" y2="140" stroke="#111111" stroke-width="3" />`,
  )

  const fields: [string, string][] = [
    ['Decision', 'APPROVED'],
    ['Claim / visa', decision.visaNo],
    ['Plate', decision.plateNo],
    ['Declaration', decision.declarationId],
    ['Claim officer', decision.officerName],
    ['Prepared', formatStamp(decision.preparedAt)],
  ]

  let y = 210
  for (const [label, value] of fields) {
    rows.push(line(y, `${label}:`, 28, 'bold', '#555555'))
    rows.push(
      `<text x="420" y="${y}" font-family="Helvetica, Arial, sans-serif" font-size="28" ` +
        `fill="#111111">${escapeXml(value)}</text>`,
    )
    y += 48
  }

  y += 24
  rows.push(line(y, 'Comments', 28, 'bold', '#555555'))
  y += 44

  const comments = decision.comments.trim()
  if (comments.length === 0) {
    rows.push(line(y, '(none)', 26, 'normal', '#555555'))
  } else {
    for (const wrapped of wrapText(comments)) {
      // Silently dropping the tail would be worse than a short image: the comments are the part a
      // claims dispute quotes. The render size is generous enough that this is a guard, not a limit
      // anyone meets in practice.
      if (y > height - 60) {
        rows.push(line(y, '… continued in the claim officer notes.', 26, 'bold', '#555555'))
        break
      }
      rows.push(line(y, wrapped, 26, 'normal'))
      y += 36
    }
  }

  return (
    `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" ` +
    `viewBox="0 0 ${width} ${height}">` +
    `<rect x="0" y="0" width="${width}" height="${height}" fill="#ffffff" />` +
    rows.join('') +
    '</svg>'
  )
}

/** The export, as the `File` the upload takes. */
export function renderApprovalToFile(
  decision: ApprovalDecision,
  render?: SvgRenderer,
): Promise<File> {
  return renderSvgToFile(
    buildApprovalSvg(decision),
    APPROVAL_RENDER,
    // Named after the declaration, not a random id: one approval per declaration by construction
    // (§5.2 approves once), and this is the name it arrives under in NEXT3's Survey folder.
    `approval-${decision.declarationId}.png`,
    render,
  )
}
