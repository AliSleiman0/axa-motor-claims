namespace Api.Infrastructure.Cleanup;

/// <summary>
/// design.md Appendix A's `Retention` section — §7.3's blob lifecycle plus the housekeeping the
/// cleanup job absorbed when it finally existed.
/// </summary>
public sealed class RetentionOptions
{
    public const string SectionName = "Retention";

    /// <summary>§7.3: a blob is deleted <c>sent_at + BlobDays</c> after NEXT3 confirmed the push.</summary>
    public int BlobDays { get; set; } = 7;

    /// <summary>
    /// §7.3's broker-media window. **Not yet enforced** — the cleanup query deletes only documents
    /// whose outbox row reads `sent`, and broker media has no outbox row at all (`push_status = n/a`),
    /// so it is structurally excluded and simply survives. Slice 5.2/5.3 owns `broker_request`'s
    /// terminal states and will add the second branch. Retaining too long is the safe side.
    /// </summary>
    public int BrokerBlobDays { get; set; } = 30;

    /// <summary>
    /// §7.3's "a blob without a row is garbage the cleanup job sweeps". The grace window matters: a
    /// blob written seconds ago is far more likely to be an upload whose transaction has not committed
    /// yet than it is to be garbage.
    /// </summary>
    public int OrphanBlobHours { get; set; } = 24;

    /// <summary>§4: `otp_challenge` is "TTL'd, purged by cleanup job" — scheduled here by slice 1.2.</summary>
    public int OtpChallengeHours { get; set; } = 24;

    /// <summary>
    /// False in tests. The same trap slice 2.2 hit with <c>OutboxWorker</c>: a live loop sweeps rows
    /// the suite is mid-assertion on, across the one database the serialized collection shares.
    /// </summary>
    public bool CleanupEnabled { get; set; } = true;

    public int PollMinutes { get; set; } = 60;
}
