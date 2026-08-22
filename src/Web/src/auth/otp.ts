/**
 * The bits S2 (sign-in) and S1 (invite activation) share: one code field, one countdown, and one set
 * of sentences for a refusal the server deliberately will not explain.
 *
 * A `.ts` module rather than a component, so both screens can use the parts they need without either
 * owning the other's flow — S1 activates an invitation and S2 signs in, and they only look alike.
 */

/** Six digits (`OtpService` generates six). `maxLength` on the field, so a paste cannot overrun it. */
export const OTP_LENGTH = 6

/**
 * How many tries a code gets before it stops working (`Auth:OtpMaxAttempts`).
 *
 * **Counted by the screen, not read from the server, and that is deliberate.** The API answers a
 * wrong code, an expired code, a used code, an exhausted code and an unknown number with one
 * identical empty 401, precisely so that nothing can be probed by trying. So the screen can honestly
 * say "a code can be tried five times" — a fact about the rule — while never claiming to know which
 * of those five things just happened. Anything more specific needs a deliberate decision on the
 * server about what the app is willing to reveal, not a guess here.
 */
export const OTP_MAX_ATTEMPTS = 5

export const OTP_REJECTED =
  'That code was not accepted. Check the last message and try again, or send a new code.'

export const OTP_EXHAUSTED =
  'A code can be tried five times, then it stops working. Send a new one and use the newest message.'

/**
 * The seconds a `429` says to wait, from its `Retry-After` header.
 *
 * §9 throttles resends (`Auth:OtpResendSeconds`) and `AuthEndpoints.TooManyRequests` puts the
 * remaining seconds in the header. Reading it rather than counting down from the configured value
 * is what keeps the countdown right **across a reload** — the phone has no idea when the code was
 * actually sent, and a guess drifts the moment somebody backgrounds the app.
 *
 * Returns null when the header is missing or unparseable, and the caller then shows no countdown at
 * all rather than an invented one.
 */
export function retryAfterSeconds(response: Response): number | null {
  const header = response.headers.get('Retry-After')
  if (!header) return null

  const seconds = Number.parseInt(header, 10)
  return Number.isFinite(seconds) && seconds > 0 ? seconds : null
}
