namespace Api.Modules.PublicSurface;

/// <summary>
/// design.md §9.1's token rules as one pure, total function. Kept free of EF, HTTP and the clock
/// so the lifecycle table can be tested exhaustively without a database — this is one of CLAUDE.md's
/// four test-first cores.
/// </summary>
public static class PublicLinkLifecycle
{
    /// <summary>
    /// "Reusable until first successful submission, then locked" (§9.1): a customer may leave and
    /// come back to an unfinished form, but each link accepts exactly one submission.
    /// </summary>
    /// <remarks>
    /// Lock is checked before expiry. Both render as the same 404 so the order is invisible from
    /// outside, but it must be *decided* rather than incidental: a submitted-then-expired link is
    /// meaningfully "used", not "timed out", and the audit trail should say so.
    /// </remarks>
    public static PublicLinkTokenStatus Evaluate(PublicLinkToken? token, DateTime now)
    {
        if (token is null)
        {
            return PublicLinkTokenStatus.NotFound;
        }

        if (token.LockedAt is not null)
        {
            return PublicLinkTokenStatus.Locked;
        }

        // '<=' matches TokenService.Rotate: the instant of expiry is already expired.
        return token.ExpiresAt <= now ? PublicLinkTokenStatus.Expired : PublicLinkTokenStatus.Valid;
    }
}
