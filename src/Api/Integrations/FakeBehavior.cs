using Microsoft.Extensions.Options;

namespace Api.Integrations;

/// <summary>
/// The shared latency + failure roll behind every fake (design.md §6.2 `Fake:FailureRate`,
/// `Fake:LatencyMs`). One implementation so "make the world flaky" is a single config change that
/// hits NEXT3 and all three senders at once.
///
/// IOptionsMonitor, not IOptions: the placeholder file is loaded with reloadOnChange, so failure
/// injection can be turned on mid-demo without a restart.
/// </summary>
public sealed class FakeBehavior
{
    private readonly IOptionsMonitor<FakeOptions> _options;
    private readonly TimeProvider _time;
    private readonly Random _random;
    private readonly Lock _gate = new();

    public FakeBehavior(IOptionsMonitor<FakeOptions> options, TimeProvider time)
        : this(options, time, Random.Shared)
    {
    }

    // Seeded Random for deterministic failure-injection tests.
    internal FakeBehavior(IOptionsMonitor<FakeOptions> options, TimeProvider time, Random random)
    {
        _options = options;
        _time = time;
        _random = random;
    }

    /// <summary>
    /// Applies the configured latency, then throws <see cref="FakeTransientException"/> with the
    /// configured probability. Call this at the top of every fake operation. Idempotent operations
    /// call <see cref="Delay"/> and <see cref="MaybeFail"/> separately so a replay can short-circuit
    /// between the two and never fail.
    /// </summary>
    public async Task Apply(string operation, CancellationToken ct)
    {
        await Delay(ct);
        MaybeFail(operation);
    }

    /// <summary>Applies the configured latency only.</summary>
    public Task Delay(CancellationToken ct)
    {
        // Only delay when asked: a FakeTimeProvider in tests never advances on its own, so an
        // unconditional delay would hang the suite.
        var latencyMs = _options.CurrentValue.LatencyMs;
        return latencyMs > 0
            ? Task.Delay(TimeSpan.FromMilliseconds(latencyMs), _time, ct)
            : Task.CompletedTask;
    }

    /// <summary>Rolls the configured failure rate only.</summary>
    public void MaybeFail(string operation)
    {
        if (ShouldFail(_options.CurrentValue.FailureRate))
        {
            throw new FakeTransientException($"Injected fake failure in '{operation}'.");
        }
    }

    private bool ShouldFail(double failureRate)
    {
        if (failureRate <= 0)
        {
            return false;
        }

        // Random is not thread-safe and two outbox workers will share this instance (slice 2.2).
        lock (_gate)
        {
            return _random.NextDouble() < Math.Min(failureRate, 1.0);
        }
    }
}
