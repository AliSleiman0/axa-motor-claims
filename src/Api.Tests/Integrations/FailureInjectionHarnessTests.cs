using Api.Integrations;
using Api.Integrations.Next3;

namespace Api.Tests.Integrations;

/// <summary>
/// Slice 1.4's definition of done: failure injection demonstrably causes retries downstream.
/// This is a throwaway harness standing in for the real outbox worker — slice 2.2 replaces it with
/// the actual §6.3 loop (backoff schedule, `failed` terminal state, two-worker READPAST test).
/// Delete this file when 2.2's outbox tests land; what it proves is a subset of theirs.
/// </summary>
public sealed class FailureInjectionHarnessTests
{
    private const string SeededVisa = "PLACEHOLDER-VISA-0001";

    [Fact]
    public async Task FlakyNext3_RetriedUntilSuccess_PushesExactlyOnce()
    {
        // Seeded Random: this is a fixed sequence of coin flips, not a flaky test.
        var (behavior, _, _) = FakeTestHarness.Build(failureRate: 0.5, seed: 20260819);
        var client = new FakeNext3Client(behavior);
        var doc = new DocumentPush(
            Next3Folders.ExpertDocuments, "PLACEHOLDER-DOC-01", "photo.jpg", "image/jpeg", "blob/photo.jpg");

        // What the outbox will do for real: same clientRef every attempt, retry until it sticks.
        const string ClientRef = "DOC-00000000-0000-0000-0000-000000000001";
        var failures = 0;
        var attempts = 0;

        while (attempts < 50)
        {
            attempts++;
            try
            {
                await client.UploadDocument(SeededVisa, doc, ClientRef, CancellationToken.None);
                break;
            }
            catch (FakeTransientException)
            {
                failures++;
            }
        }

        Assert.True(failures > 0, "Failure injection never fired — the harness would prove nothing.");
        Assert.True(attempts < 50, "Never succeeded within the attempt budget.");

        // The point of the whole exercise: retries after failure do not duplicate the document.
        Assert.Single(client.RecordedDocuments);
    }
}
