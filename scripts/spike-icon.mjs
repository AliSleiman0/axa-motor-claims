/*
 * Generates `src/Web/public/icon-512.png`, the PWA home-screen icon (slice 6.3a).
 *
 * It exists because **iOS will not use an SVG for a home-screen icon**, and `public/favicon.svg` is
 * all this app had. Without a PNG, "Add to Home Screen" produces a screenshot of the page — which
 * makes the one check that matters on iOS (does a non-technical field user end up with something
 * that looks like an app?) impossible to judge. HANDOFF §4's whole argument against a bare PWA is
 * about that install completing, so the icon is part of the evidence, not decoration.
 *
 * No `canvas` package: CLAUDE.md keeps one out of this repo on purpose, so this writes the PNG
 * bytes directly — the same deflate + CRC32 chunk encoder `scripts/demo-media.mjs` already uses, for
 * the same reason.
 *
 * The mark is a camera aperture on `--accent-600`, read out of `src/Web/src/styles/tokens.css`.
 * **It is deliberately not an AXA logo.** AXA has supplied no brand assets (tokens.css: "the accent
 * ramp is the only brand-swappable slot"), and inventing one would be exactly the client-data
 * fabrication CLAUDE.md forbids. The day a real icon arrives it replaces this file and nothing else.
 *
 * Usage: node scripts/spike-icon.mjs [output-path]
 */

import { deflateSync } from 'node:zlib'
import { writeFileSync } from 'node:fs'

const SIZE = 512

// src/Web/src/styles/tokens.css — --accent-600 and --on-accent. Not invented values.
const ACCENT = [0x2e, 0x6b, 0xb8]
const ON_ACCENT = [0xff, 0xff, 0xff]

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

const centre = (SIZE - 1) / 2
const outerR = SIZE * 0.30
const innerR = SIZE * 0.19
const dotR = SIZE * 0.075

/**
 * A maskable icon: Android crops to a circle of the middle 80%, so the mark stays well inside that
 * and the accent runs to all four edges rather than sitting on a transparent square.
 */
function pixel(x, y) {
  const dx = x - centre
  const dy = y - centre
  const d = Math.hypot(dx, dy)

  // The aperture: a ring, plus a filled centre, with six blades cut out of the ring so it reads as a
  // lens rather than a target at 60 px.
  const inRing = d <= outerR && d >= innerR
  const blade = ((Math.atan2(dy, dx) + Math.PI * 2) % (Math.PI / 3)) < 0.16
  if ((inRing && !blade) || d <= dotR) return ON_ACCENT
  return ACCENT
}

const stride = SIZE * 3 + 1
const raw = Buffer.alloc(stride * SIZE)
for (let y = 0; y < SIZE; y += 1) {
  const row = y * stride
  raw[row] = 0 // filter: None
  for (let x = 0; x < SIZE; x += 1) {
    const [r, g, b] = pixel(x, y)
    const at = row + 1 + x * 3
    raw[at] = r
    raw[at + 1] = g
    raw[at + 2] = b
  }
}

const ihdr = Buffer.alloc(13)
ihdr.writeUInt32BE(SIZE, 0)
ihdr.writeUInt32BE(SIZE, 4)
ihdr[8] = 8 // bit depth
ihdr[9] = 2 // colour type: truecolour
const png = Buffer.concat([
  Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
  chunk('IHDR', ihdr),
  chunk('IDAT', deflateSync(raw, { level: 9 })),
  chunk('IEND', Buffer.alloc(0)),
])

const out = process.argv[2] ?? new URL('../src/Web/public/icon-512.png', import.meta.url).pathname
writeFileSync(out, png)
console.log(`${out} — ${SIZE}x${SIZE}, ${png.length} bytes`)
