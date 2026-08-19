using Api.Integrations;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Api.Tests.Integrations;

/// <summary>
/// An IOptionsMonitor whose value can be changed mid-test — the production code reads
/// CurrentValue on every call so failure injection can be turned on and off inside one scenario.
/// </summary>
public sealed class MutableOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>Builds a FakeBehavior with a seeded Random so failure injection is deterministic.</summary>
public static class FakeTestHarness
{
    public static (FakeBehavior Behavior, MutableOptionsMonitor<FakeOptions> Options, FakeTimeProvider Time) Build(
        double failureRate = 0,
        int latencyMs = 0,
        int seed = 1234)
    {
        var options = new MutableOptionsMonitor<FakeOptions>(
            new FakeOptions { FailureRate = failureRate, LatencyMs = latencyMs });
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        return (new FakeBehavior(options, time, new Random(seed)), options, time);
    }
}
