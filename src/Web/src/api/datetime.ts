/**
 * The API's timestamps are UTC, but they do not all say so.
 *
 * Values the server has just computed serialize with a `Z` (DateTimeKind.Utc), while the same value
 * read back out of SQL reaches JSON as DateTimeKind.Unspecified and arrives bare. Left alone, the
 * browser reads a bare timestamp as local time — so an expert in GST would be shown an arrival four
 * hours off, on the one screen whose entire job is recording when they arrived.
 */
export function toUtcDate(iso: string): Date {
  const hasZone = /[Zz]$|[+-]\d\d:?\d\d$/.test(iso)
  return new Date(hasZone ? iso : `${iso}Z`)
}

export function formatDateTime(iso: string): string {
  return toUtcDate(iso).toLocaleString()
}
