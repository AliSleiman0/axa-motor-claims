namespace Api.Outbox;

/// <summary>
/// Bound from the "Outbox" section of the placeholder config (design.md Appendix A). These are
/// engineering knobs rather than client data, and they live in the placeholder file for the same
/// reason `Fake:*` does: it is loaded with reloadOnChange, so the backoff can be retuned when #33
/// answers NEXT3's maintenance windows, and the week-4 demo can change behaviour without a restart.
/// </summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>§6.3: "after ~8 attempts → failed". The 8th failure is terminal.</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>§6.3's backoff ceiling. A config knob explicitly so #33 can retune it.</summary>
    public int BackoffCeilingHours { get; set; } = 6;

    /// <summary>§6.3's `UPDATE TOP (10)`.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>§6.3: the worker loop runs "every ~30s".</summary>
    public int PollSeconds { get; set; } = 30;

    /// <summary>
    /// How long a claimed row stays claimed. A worker that dies between claiming a row and writing
    /// its outcome would otherwise strand it in `processing` forever — never retried, and invisible
    /// to A2's `failed` list. Re-pushing a row whose worker was merely slow is safe because every
    /// push carries a stable clientRef (§6.3 idempotency).
    /// </summary>
    public int LeaseSeconds { get; set; } = 300;

    /// <summary>
    /// Whether the background loop runs. Tests set this false (via the `Outbox__WorkerEnabled` env
    /// var) and drive <see cref="OutboxProcessor.RunOnce"/> explicitly — an autonomous 30-second loop
    /// against the shared test database would race every test in the collection.
    /// </summary>
    public bool WorkerEnabled { get; set; } = true;
}
