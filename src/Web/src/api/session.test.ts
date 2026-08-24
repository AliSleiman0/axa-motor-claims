import { describe, expect, it } from 'vitest'
import { homePathFor } from './session'

describe('homePathFor', () => {
  it('sends each role with a screen to its own screen', () => {
    // The keys are the server's role strings verbatim (`UserRoles`, check-constrained on
    // `app_user.role`). Snake case: `claim_officer`, not `claimOfficer`. A typo here is a claim
    // officer landing on the admin screens and meeting a wall of 403s — which is exactly what both
    // of §5.2's roles did until slice 4.2.
    expect(homePathFor('expert')).toBe('/expert')
    expect(homePathFor('garage')).toBe('/garage')
    expect(homePathFor('claim_officer')).toBe('/officer')
  })

  it('sends the paths the push notifications already point at', () => {
    // Slice 4.1's `DeclarationService` pushes `/garage/{id}` and `/officer/{id}` as the click target
    // and `sw.js` opens `data.url`. These two prefixes are therefore a contract with the server, not
    // a naming choice — renaming either route silently breaks §8's popups, with nothing going red.
    expect(homePathFor('garage')).toBe('/garage')
    expect(homePathFor('claim_officer')).toBe('/officer')

    // Slice 5.2. This line replaces one asserting the fallback, which was pinning a placeholder
    // rather than a rule: `/broker` is B1, and it is also 5.3's push target, so it is a contract in
    // the same way the two above are.
    expect(homePathFor('broker')).toBe('/broker')
  })

  it('falls back to admin for a role with no screen yet, and for no role at all', () => {
    // A token that cannot be read is routed as "no role": the admin screens' own calls will 401 and
    // bounce to /login. Every role in §2 now has a home of its own.
    expect(homePathFor('admin')).toBe('/admin/experts')
    expect(homePathFor(null)).toBe('/admin/experts')
    expect(homePathFor('PLACEHOLDER-not-a-role')).toBe('/admin/experts')
  })
})
