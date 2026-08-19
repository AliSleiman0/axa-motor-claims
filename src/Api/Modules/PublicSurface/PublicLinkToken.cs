namespace Api.Modules.PublicSurface;

/// <summary>
/// design.md §4 <c>public_link_token</c> — the Option 2 capability. Only the SHA-256 hash is
/// stored (§9.1), so a database leak yields no working links.
/// </summary>
public sealed class PublicLinkToken
{
    public Guid Id { get; set; }
    public Guid BrokerRequestId { get; set; }

    /// <summary>SHA-256 hex of the raw token. The raw value exists only in the broker's hands.</summary>
    public required string TokenHash { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>Set on the first successful submission; the token is dead from that moment (§9.1).</summary>
    public DateTime? LockedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// §9.1's "each link accepts exactly one submission", moved out of application code and into the
    /// schema. Reading <see cref="LockedAt"/> and then writing it is two steps, and two simultaneous
    /// submissions both pass the read before either writes — so the guarantee cannot live in the
    /// check. The database decides instead: the second write loses and is rendered as the same 404.
    /// </summary>
    public byte[]? Version { get; set; }
}

/// <summary>
/// The outcome of §9.1's token check. Only <see cref="Valid"/> is actionable — every other value
/// is rendered as the same uniform 404, so the caller cannot tell them apart.
/// </summary>
public enum PublicLinkTokenStatus
{
    NotFound,
    Expired,
    Locked,
    Valid,
}
