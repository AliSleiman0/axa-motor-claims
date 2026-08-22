import { describe, expect, it } from 'vitest'
import { ApiError } from '../api/client'
import { describeSaveError } from './saveErrors'

/**
 * A1's save refusals (pass 3's only finding that was a defect in shipped behaviour).
 *
 * The form used to render `Save failed (409): {"error":"duplicate_next3_id"}` — a raw response body,
 * shown to an administrator, naming a column rather than the thing they just did.
 */
describe('describeSaveError', () => {
  it.each([
    [400, 'invalid_phone', /country code/],
    [409, 'duplicate_phone', /already has that phone number/],
    [409, 'duplicate_next3_id', /already uses that NEXT3 ID/],
  ])('turns %i %s into a sentence', (status, code, expected) => {
    const message = describeSaveError(new ApiError(status, JSON.stringify({ error: code })))

    expect(message).toMatch(expected)
    // The point of the fix: no machine code and no JSON reaches the screen.
    expect(message).not.toContain(code)
    expect(message).not.toContain('{')
  })

  it('keeps the status for a refusal nobody has written words for', () => {
    // The house convention for an unmapped error. A wrong sentence would be worse than a number.
    expect(describeSaveError(new ApiError(409, JSON.stringify({ error: 'something_new' })))).toBe(
      'The profile was not saved (409).',
    )
  })

  it('survives a body that is not JSON at all', () => {
    // A 500 from outside the endpoint's own refusals returns an HTML error page, and a screen that
    // throws while rendering an error message shows nothing at all.
    expect(describeSaveError(new ApiError(500, '<html>Server Error</html>'))).toBe(
      'The profile was not saved (500).',
    )
  })
})
