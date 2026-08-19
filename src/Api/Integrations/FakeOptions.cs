namespace Api.Integrations;

/// <summary>
/// Failure injection for every fake (design.md §6.2). Lives in the placeholder config so the
/// week-4 demo can "kill NEXT3 mid-flow" and show the queue drain on recovery without code changes.
/// Defaults are zero: existing flows stay deterministic unless a test or a demo asks for chaos.
/// </summary>
public sealed class FakeOptions
{
    public const string SectionName = "Fake";

    /// <summary>Probability [0,1] that a fake call throws <see cref="FakeTransientException"/>.</summary>
    public double FailureRate { get; set; }

    /// <summary>Artificial latency before each fake call completes.</summary>
    public int LatencyMs { get; set; }
}
