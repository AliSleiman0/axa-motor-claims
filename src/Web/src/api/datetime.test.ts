import { describe, expect, it } from 'vitest'
import { toUtcDate } from './datetime'

describe('toUtcDate', () => {
  it('reads a bare API timestamp as UTC', () => {
    // A timestamp read back out of SQL reaches JSON without a 'Z'. Parsed as local time it would
    // move by the machine's offset, which on the Arrived screen is the one error nobody would spot.
    expect(toUtcDate('2026-08-20T09:30:00').toISOString()).toBe('2026-08-20T09:30:00.000Z')
  })

  it('leaves a timestamp that already declares its zone alone', () => {
    expect(toUtcDate('2026-08-20T09:30:00Z').toISOString()).toBe('2026-08-20T09:30:00.000Z')
    expect(toUtcDate('2026-08-20T13:30:00+04:00').toISOString()).toBe('2026-08-20T09:30:00.000Z')
  })

  it('keeps sub-second precision', () => {
    expect(toUtcDate('2026-08-20T09:30:00.125').toISOString()).toBe('2026-08-20T09:30:00.125Z')
  })
})
