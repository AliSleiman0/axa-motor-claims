using Api.Modules.PublicSurface;

namespace Api.Tests.Unit;

/// <summary>
/// design.md §9.1's token lifecycle table, exhaustively. Pure function, no host, no database —
/// every case here is a rule the public surface must not lose during weeks 5–6.
/// </summary>
public class PublicLinkLifecycleTests
{
    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    private static PublicLinkToken Token(DateTime expiresAt, DateTime? lockedAt = null) => new()
    {
        Id = Guid.CreateVersion7(),
        BrokerRequestId = Guid.CreateVersion7(),
        TokenHash = new string('a', 64),
        ExpiresAt = expiresAt,
        LockedAt = lockedAt,
        CreatedAt = Now.AddDays(-1),
    };

    [Fact]
    public void UnknownToken_IsNotFound()
    {
        Assert.Equal(PublicLinkTokenStatus.NotFound, PublicLinkLifecycle.Evaluate(null, Now));
    }

    [Fact]
    public void LiveUnlockedToken_IsValid()
    {
        var token = Token(Now.AddDays(7));

        Assert.Equal(PublicLinkTokenStatus.Valid, PublicLinkLifecycle.Evaluate(token, Now));
    }

    [Fact]
    public void AtTheInstantOfExpiry_IsExpired()
    {
        var token = Token(Now);

        Assert.Equal(PublicLinkTokenStatus.Expired, PublicLinkLifecycle.Evaluate(token, Now));
    }

    [Fact]
    public void OneSecondBeforeExpiry_IsStillValid()
    {
        var token = Token(Now.AddSeconds(1));

        Assert.Equal(PublicLinkTokenStatus.Valid, PublicLinkLifecycle.Evaluate(token, Now));
    }

    [Fact]
    public void LockedToken_IsLocked_EvenWhileStillWithinValidity()
    {
        var token = Token(Now.AddDays(7), lockedAt: Now.AddMinutes(-1));

        Assert.Equal(PublicLinkTokenStatus.Locked, PublicLinkLifecycle.Evaluate(token, Now));
    }

    [Fact]
    public void LockedAndExpiredToken_ReportsLocked_AndIsNeverValid()
    {
        var token = Token(Now.AddDays(-1), lockedAt: Now.AddDays(-2));

        var status = PublicLinkLifecycle.Evaluate(token, Now);

        Assert.Equal(PublicLinkTokenStatus.Locked, status);
        Assert.NotEqual(PublicLinkTokenStatus.Valid, status);
    }

    [Fact]
    public void RepeatedEvaluationBeforeLock_StaysValid()
    {
        // §9.1's "reusable until first successful submission": re-opening an unfinished form is
        // the normal case, not an abuse signal.
        var token = Token(Now.AddDays(7));

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(PublicLinkTokenStatus.Valid, PublicLinkLifecycle.Evaluate(token, Now.AddHours(i)));
        }
    }

    [Fact]
    public void ExpiryIsEvaluatedAgainstTheSuppliedClock_NotWallClock()
    {
        var token = Token(Now.AddDays(7));

        Assert.Equal(PublicLinkTokenStatus.Valid, PublicLinkLifecycle.Evaluate(token, Now));
        Assert.Equal(PublicLinkTokenStatus.Expired, PublicLinkLifecycle.Evaluate(token, Now.AddDays(8)));
    }
}
