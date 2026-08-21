import { describe, expect, it } from 'vitest'
import { urlBase64ToUint8Array } from './urlBase64'

describe('urlBase64ToUint8Array', () => {
  it('decodes a padded-length string', () => {
    // "AQID" is 4 characters, already a multiple of 4, so no padding is added.
    expect([...urlBase64ToUint8Array('AQID')]).toEqual([1, 2, 3])
  })

  it('decodes a real-length VAPID key, which is not a multiple of four', () => {
    // The case the whole function exists for. A P-256 public key is the 65-byte uncompressed point,
    // which base64url-encodes to 87 characters — and 87 % 4 === 3, so without the padding this call
    // either throws or silently drops the tail depending on the engine. A key one byte short is
    // still accepted by pushManager.subscribe() and then fails to decrypt on the device, where
    // nothing server-side can see it.
    const key = new Uint8Array(65)
    for (let i = 0; i < key.length; i += 1) key[i] = i

    const encoded = base64UrlEncode(key)
    expect(encoded).toHaveLength(87)
    expect(encoded).not.toContain('=')

    const decoded = urlBase64ToUint8Array(encoded)
    expect(decoded).toHaveLength(65)
    expect([...decoded]).toEqual([...key])
  })

  it('round-trips every length that needs a different amount of padding', () => {
    // 0, 1, 2 and 3 characters of padding respectively — the whole state space of the `%` above.
    for (const length of [64, 65, 66, 67]) {
      const bytes = new Uint8Array(length).map((_, i) => (i * 7) % 256)
      expect([...urlBase64ToUint8Array(base64UrlEncode(bytes))]).toEqual([...bytes])
    }
  })

  it('translates the url-safe alphabet back', () => {
    // 0xFB 0xFF decodes from '+/' in standard base64 and '-_' in base64url; a function that forgot
    // the replace would throw or produce different bytes here.
    expect([...urlBase64ToUint8Array('-_8')]).toEqual([251, 255])
  })
})

/** The encoder a push service uses, written out so the test does not depend on the code under test. */
function base64UrlEncode(bytes: Uint8Array): string {
  let binary = ''
  for (const byte of bytes) binary += String.fromCharCode(byte)
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}
