/** The two values the Arrived push needs from the handset (design.md §5.1; the date and time are the server's). */
export interface Position {
  latitude: number
  longitude: number
}

export type GeolocationFailure = 'denied' | 'unavailable' | 'timeout' | 'unsupported'

export class GeolocationError extends Error {
  readonly reason: GeolocationFailure

  constructor(reason: GeolocationFailure) {
    super(`Geolocation failed: ${reason}`)
    this.reason = reason
  }
}

/**
 * What E2 shows instead of sending the arrival. §5.1: "geolocation-denied shows a blocking
 * explanation (arrival without location is not sent — the BRD requires all three values)". The
 * explanation has to say why, or the expert will just press the button again.
 */
export const GEOLOCATION_EXPLANATIONS: Record<GeolocationFailure, string> = {
  denied:
    'Arrival was not sent. AXA records where the expert arrived, so this app cannot report an ' +
    'arrival without a location. Allow location access for this site and press Arrived again.',
  unavailable:
    'Arrival was not sent. This device could not determine a location. Move somewhere with a ' +
    'clearer view of the sky and press Arrived again.',
  timeout:
    'Arrival was not sent. Finding a location took too long. Press Arrived again once the device ' +
    'has a signal.',
  unsupported:
    'Arrival was not sent. This browser does not provide location. Use the AXA app on the phone ' +
    'you were dispatched with.',
}

/**
 * What the native shell says instead, for the one code whose browser copy is wrong there.
 *
 * **Slice 6.3a measured this on the Samsung: with location services switched off, the Android
 * WebView reports code 3, `TIMEOUT`** — not code 2, `POSITION_UNAVAILABLE`. So the expert was
 * correctly told what the platform said and incorrectly told what to do: "press Arrived again once
 * the device has a signal" can never work, because no amount of signal helps when the setting is
 * off. A web page cannot read the system location setting, so the honest fix is copy that names the
 * likelier cause first while still covering a genuine slow fix.
 *
 * Only `timeout` is overridden. The other three codes mean the same thing on both platforms, and a
 * second full table would be two copies to keep in step.
 */
export const NATIVE_GEOLOCATION_OVERRIDES: Partial<Record<GeolocationFailure, string>> = {
  timeout:
    'Arrival was not sent. This device did not return a location — on Android that usually means ' +
    'Location is switched off. Turn it on in Settings › Location, then press Arrived again.',
}

/**
 * The explanation to show for a failure, given whether the app is running in the native shell.
 *
 * `GEOLOCATION_EXPLANATIONS` stays exported and stays the browser's answer, so the tests that pin
 * those strings are untouched and there is still one table rather than two.
 */
export function explainGeolocationFailure(reason: GeolocationFailure, native: boolean): string {
  return (native ? NATIVE_GEOLOCATION_OVERRIDES[reason] : undefined) ?? GEOLOCATION_EXPLANATIONS[reason]
}

const OPTIONS: PositionOptions = { enableHighAccuracy: true, timeout: 15_000, maximumAge: 0 }

/**
 * Promisifies the browser's one-shot position request. The `geolocation` parameter exists so a test
 * can hand in a stub; production always takes the default.
 */
export function requestPosition(
  geolocation: Geolocation | undefined = globalThis.navigator?.geolocation,
): Promise<Position> {
  if (!geolocation) {
    return Promise.reject(new GeolocationError('unsupported'))
  }

  return new Promise((resolve, reject) => {
    geolocation.getCurrentPosition(
      (position) =>
        resolve({ latitude: position.coords.latitude, longitude: position.coords.longitude }),
      (error) => reject(new GeolocationError(reasonOf(error))),
      OPTIONS,
    )
  })
}

// Compared as numbers rather than through GeolocationPositionError.PERMISSION_DENIED, because the
// callback is handed whatever object the platform (or a test stub) built, and only the code is
// guaranteed to be there.
function reasonOf(error: GeolocationPositionError): GeolocationFailure {
  if (error.code === 1) return 'denied'
  if (error.code === 3) return 'timeout'
  return 'unavailable'
}
