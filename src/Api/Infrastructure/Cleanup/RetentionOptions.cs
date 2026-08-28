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
    /// §7.3's broker-media window, enforced by <c>BrokerMediaCleanupTask</c> since slice 5.2. Broker
    /// media has no outbox row at all (`push_status = n/a`), so it is structurally invisible to the
    /// first sweep; this window is measured from `broker_request.emailed_at` instead — the moment the
    /// documents left as attachments, which is the thing that licenses deleting them.
    /// </summary>
    public int BrokerBlobDays { get; set; } = 30;

    /// <summary>
    /// §7.3's rejected-declaration window (slice 7.2), measured from `decided_at`.
    ///
    /// Rejection is terminal with no resubmit edge (§5.2), so nothing ever moves those documents on:
    /// they stay `deferred`, they never acquire an outbox row, the first sweep cannot see them and the
    /// orphan sweep spares them because a live `document` row still claims the bytes. Left alone they
    /// are a permanent store of accident photographs, which neither §7.3's "flat forever" sizing nor
    /// §9's "the app is deliberately not a long-term PII store" survives.
    ///
    /// **Thirty days is a placeholder, not an answer.** How long AXA may keep a rejected claim's
    /// photographs is #4/#22. What this slice buys is the mechanism; the number is one config edit.
    /// </summary>
    public int RejectedDeclarationBlobDays { get; set; } = 30;

    /// <summary>
    /// §7.3's abandoned-Option-2 window (slice 7.2), measured from the request's **newest public link
    /// token's** `expires_at` — not from a state, because `expired` is computed in B1's projection and
    /// never written, so there is no column a sweep could test.
    ///
    /// The case: a member of the public photographs their identity card and up to five sides of their
    /// car, then never presses Send. `emailed_at` stays null for ever, so the broker sweep's predicate
    /// never matches and those bytes sit in the transit container with nothing to move them on.
    ///
    /// **The window is also what protects a resendable failed send.** A submitted request whose email
    /// failed has `emailed_at` null too and looks identical on that column alone; its token expires on
    /// the same schedule, so waiting for the window to pass is what keeps B1's **Resend** working —
    /// pinned by its own test rather than left as an inference. Placeholder value, #4/#22.
    /// </summary>
    public int AbandonedRequestBlobDays { get; set; } = 30;

    /// <summary>
    /// How long a revoked `device_token` / `push_subscription` row is kept before it is deleted
    /// (slice 7.2). Revoked rows are kept at all so a `notification` row naming a device still
    /// resolves to something, and pruned eventually because the registry is otherwise a table that
    /// only grows — every OEM battery kill, every 410 from a push service and every handset that
    /// changes hands adds one. Placeholder value, #4/#22.
    /// </summary>
    public int RevokedDeviceDays { get; set; } = 30;

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
