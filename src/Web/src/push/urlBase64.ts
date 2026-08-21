/**
 * base64url → bytes, for the VAPID application server key.
 *
 * `pushManager.subscribe()` wants `applicationServerKey` as raw bytes, and the server serves the key
 * as base64url (that is how VAPID keys are written everywhere — RFC 8292). Hence this function, which
 * every web-push tutorial also carries; it is here rather than in `src/api/` because nothing outside
 * push has ever needed base64.
 *
 * **The padding is the part worth writing a test for.** `src/api/session.ts` decodes JWT payloads
 * with a bare `atob(...replace(...))` and a comment saying atob tolerates missing padding — true
 * enough for that use, and not safe to copy here: a P-256 public key is the 65-byte uncompressed
 * point, which base64url-encodes to **87 characters**, and 87 is not a multiple of 4. Left unpadded,
 * `atob` either throws or silently drops the tail depending on the engine — and a key that is one
 * byte short is accepted by `subscribe()` and then fails to decrypt on the device, with nothing
 * server-side to see.
 */
export function urlBase64ToUint8Array(base64Url: string): Uint8Array<ArrayBuffer> {
  const padding = '='.repeat((4 - (base64Url.length % 4)) % 4)
  const base64 = (base64Url + padding).replace(/-/g, '+').replace(/_/g, '/')

  const raw = atob(base64)
  // Backed by an explicit ArrayBuffer so the type is `Uint8Array<ArrayBuffer>` rather than
  // `Uint8Array<ArrayBufferLike>`: `applicationServerKey` will not accept the latter, because a
  // SharedArrayBuffer-backed view is not a valid BufferSource.
  const bytes = new Uint8Array(new ArrayBuffer(raw.length))
  for (let i = 0; i < raw.length; i += 1) {
    bytes[i] = raw.charCodeAt(i)
  }

  return bytes
}
