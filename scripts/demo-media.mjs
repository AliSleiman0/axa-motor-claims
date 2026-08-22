// Generates the files the week-4 demo picks up (slice 4.3). Nothing binary enters the repo: the
// demo needs a photo that passes design.md §7.2's gate and one that fails it, and both have to be
// real files a real Chrome will decode — the suite's synthetic JPEGs are header-only and Chrome
// refuses them (2.5's browser-pass note).
//
// The two images are built against how the gate actually measures, not by eye:
//   * `sharp.png`   — a hard-edged checkerboard. `downscaleTo` is nearest-neighbour, so the edges
//                     survive the reduction to `Clarity.BlurAnalysisMaxEdge` and the Laplacian
//                     variance lands in the tens of thousands, far above the threshold.
//   * `blurry.png`  — a smooth horizontal gradient. Its second derivative is ~0 everywhere, so the
//                     variance is ~0: it clears the resolution floor and fails on sharpness alone,
//                     which is the refusal the demo needs to show (a too-small image would fail for
//                     the wrong reason and tell the expert the wrong thing).
// Both are 1600x1200 — above `Clarity.MinWidth/MinHeight` (1024x768), so neither can fail the floor.
//
// Usage: node scripts/demo-media.mjs <output-directory>

import { deflateSync } from 'node:zlib'
import { mkdirSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'

const WIDTH = 1600
const HEIGHT = 1200

const outDir = process.argv[2]
if (!outDir) {
  console.error('usage: node scripts/demo-media.mjs <output-directory>')
  process.exit(1)
}

const CRC_TABLE = (() => {
  const table = new Int32Array(256)
  for (let n = 0; n < 256; n += 1) {
    let c = n
    for (let k = 0; k < 8; k += 1) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1
    table[n] = c
  }
  return table
})()

function crc32(buffer) {
  let c = 0xffffffff
  for (const byte of buffer) c = CRC_TABLE[(c ^ byte) & 0xff] ^ (c >>> 8)
  return (c ^ 0xffffffff) >>> 0
}

function chunk(type, data) {
  const length = Buffer.alloc(4)
  length.writeUInt32BE(data.length)
  const body = Buffer.concat([Buffer.from(type, 'latin1'), data])
  const crc = Buffer.alloc(4)
  crc.writeUInt32BE(crc32(body))
  return Buffer.concat([length, body, crc])
}

/** @param shade (x, y) => 0-255 grey value. */
function png(shade) {
  // One filter byte (0 = None) per scanline, then RGB triples.
  const stride = WIDTH * 3 + 1
  const raw = Buffer.alloc(stride * HEIGHT)
  for (let y = 0; y < HEIGHT; y += 1) {
    const row = y * stride
    raw[row] = 0
    for (let x = 0; x < WIDTH; x += 1) {
      const value = shade(x, y)
      const at = row + 1 + x * 3
      raw[at] = value
      raw[at + 1] = value
      raw[at + 2] = value
    }
  }

  const ihdr = Buffer.alloc(13)
  ihdr.writeUInt32BE(WIDTH, 0)
  ihdr.writeUInt32BE(HEIGHT, 4)
  ihdr[8] = 8 // bit depth
  ihdr[9] = 2 // colour type: truecolour
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', deflateSync(raw, { level: 6 })),
    chunk('IEND', Buffer.alloc(0)),
  ])
}

/** A minimal one-page PDF. Byte offsets are computed, not guessed — a wrong xref is a file Chrome
 *  will open and the server's sniffer will still accept, which would hide a real problem. */
function pdf(title) {
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>',
    null, // the content stream, built below
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
  ]
  const text = `BT /F1 18 Tf 60 760 Td (${title}) Tj ET`
  objects[3] = `<< /Length ${text.length} >>\nstream\n${text}\nendstream`

  let body = '%PDF-1.4\n'
  const offsets = []
  objects.forEach((object, index) => {
    offsets.push(body.length)
    body += `${index + 1} 0 obj\n${object}\nendobj\n`
  })

  const startXref = body.length
  body += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`
  for (const offset of offsets) body += `${String(offset).padStart(10, '0')} 00000 n \n`
  body += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${startXref}\n%%EOF\n`
  return Buffer.from(body, 'latin1')
}

mkdirSync(outDir, { recursive: true })

// 16px squares: still 5px after the reduction to a 512px edge, so the edges are real edges and not
// a moire pattern the downscale invented.
const checker = (x, y) => ((x >> 4) + (y >> 4)) % 2 === 0 ? 20 : 235
const gradient = (x) => Math.round(20 + (x * 215) / (WIDTH - 1))

const files = [
  ['sharp-car-photo.png', png(checker)],
  ['blurry-car-photo.png', png(gradient)],
  ['insured-document.pdf', pdf('PLACEHOLDER insured document - demo only')],
  ['expert-report.pdf', pdf('PLACEHOLDER expert report - demo only')],
  ['garage-invoice.pdf', pdf('PLACEHOLDER garage invoice - demo only')],
]

for (const [name, bytes] of files) {
  writeFileSync(join(outDir, name), bytes)
  console.log(`${name}  ${bytes.length} bytes`)
}
