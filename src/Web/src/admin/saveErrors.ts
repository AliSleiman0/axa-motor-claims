import { ApiError } from '../api/client'

/**
 * A1's three save refusals, as sentences (pass 3's only finding that is a defect in shipped
 * behaviour).
 *
 * The form used to render `Save failed (409): {"error":"duplicate_next3_id"}` — a raw response body,
 * shown to an administrator, naming a column rather than the thing they did. All three of these are
 * ordinary mistakes somebody makes while typing a profile in, and each has an obvious next action,
 * so each gets a sentence that names it.
 *
 * The codes are the server's, from `AdminProfileEndpoints`: `invalid_phone` (400), `duplicate_phone`
 * (409) and `duplicate_next3_id` (409). Anything unmapped keeps the status in parentheses, which is
 * the house convention for an error nobody has written words for yet — a wrong sentence would be
 * worse than a number.
 */
const SENTENCES: Record<string, string> = {
  invalid_phone:
    'That phone number is not in the right format. Enter it with the country code, like +999 000 000 001.',
  duplicate_phone:
    'Somebody already has that phone number. Every profile signs in with its own number, so find the existing profile rather than creating a second one.',
  duplicate_next3_id:
    'Another profile already uses that NEXT3 ID. Each one maps to a single record in NEXT3, so check the ID before saving.',
}

export function describeSaveError(error: ApiError): string {
  const code = errorCode(error.body)
  const sentence = code ? SENTENCES[code] : undefined
  return sentence ?? `The profile was not saved (${error.status}).`
}

/**
 * The API answers `{ "error": "duplicate_phone" }`. Parsing is wrapped because a 500 from anywhere
 * outside the endpoint's own refusals returns an HTML error page, and a screen that throws while
 * rendering an error message shows nothing at all.
 */
function errorCode(body: string): string | null {
  try {
    const parsed = JSON.parse(body) as { error?: unknown }
    return typeof parsed.error === 'string' ? parsed.error : null
  } catch {
    return null
  }
}
