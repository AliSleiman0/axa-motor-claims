using System.Security.Cryptography;
using Api.Infrastructure;
using Api.Modules.Broker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Api.Modules.PublicSurface;

/// <summary>The raw token, returned exactly once at issue time. Never stored, never logged.</summary>
public sealed record IssuedPublicLink(Guid BrokerRequestId, Guid TokenId, string RawToken, DateTime ExpiresAt);

/// <summary>A resolved token plus the request it points at — only ever built for a <c>Valid</c> token.</summary>
public sealed record ResolvedPublicLink(PublicLinkToken Token, BrokerRequest Request);

/// <summary>
/// design.md §9.1's token lifecycle against the database. The decision itself lives in
/// <see cref="PublicLinkLifecycle"/>; this type only loads, persists and times it.
/// </summary>
public sealed class PublicLinkTokenService(
    AppDbContext db, IOptionsMonitor<PublicLinkOptions> options, TimeProvider time)
{
    /// <summary>
    /// 256 bits from a CSPRNG (§9.1). At that width the link is not guessable, which is why the
    /// design deliberately carries no CAPTCHA: possession of the link is the gate.
    /// </summary>
    public IssuedPublicLink Issue(BrokerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var token = new PublicLinkToken
        {
            Id = Guid.CreateVersion7(),
            BrokerRequestId = request.Id,
            TokenHash = TokenHashing.Hash(raw),
            ExpiresAt = Now().AddDays(options.CurrentValue.ValidityDays),
            CreatedAt = Now(),
        };
        db.PublicLinkTokens.Add(token);

        // No SaveChanges: the caller commits the request row and this token together.
        return new IssuedPublicLink(request.Id, token.Id, raw, token.ExpiresAt);
    }

    /// <summary>
    /// Resolves a raw token, or returns why it cannot be used. Callers must render every
    /// non-<c>Valid</c> status as the same 404 — the status exists for the audit trail, not the wire.
    /// </summary>
    public async Task<(PublicLinkTokenStatus Status, ResolvedPublicLink? Link)> Resolve(
        string rawToken, CancellationToken ct)
    {
        var hash = TokenHashing.Hash(rawToken);
        var token = await db.PublicLinkTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);

        var status = PublicLinkLifecycle.Evaluate(token, Now());
        if (status != PublicLinkTokenStatus.Valid || token is null)
        {
            return (status, null);
        }

        var request = await db.BrokerRequests.SingleAsync(r => r.Id == token.BrokerRequestId, ct);
        return (status, new ResolvedPublicLink(token, request));
    }

    /// <summary>
    /// First open moves the request to <c>customer_in_progress</c>; later opens are deliberately
    /// no-ops so returning to an unfinished form does not rewrite history (§5.3 P1).
    /// </summary>
    public bool MarkOpened(ResolvedPublicLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        if (link.Request.State != BrokerRequestState.LinkIssued)
        {
            return false;
        }

        link.Request.OpenByCustomer();
        return true;
    }

    /// <summary>
    /// Locks the token and moves the request to <c>ready_to_send</c> (§5.3 P1 submit). Does not
    /// save — the caller commits the field values, the lock and the audit row in one transaction.
    /// </summary>
    public void Lock(ResolvedPublicLink link)
    {
        ArgumentNullException.ThrowIfNull(link);

        var now = Now();
        link.Token.LockedAt = now;
        link.Request.ReadyToSend(now);
    }

    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
