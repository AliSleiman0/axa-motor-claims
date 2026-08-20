using Api.Integrations;
using Api.Integrations.Next3;

namespace Api.Tests.Integrations;

/// <summary>
/// The fake's contract (design.md §6.2). The dedupe behaviour here is what slice 2.2's outbox tests
/// are written against, so these tests are guarding an interface, not an implementation detail.
/// </summary>
public sealed class FakeNext3ClientTests
{
    private const string SeededVisa = "PLACEHOLDER-VISA-0001";

    [Fact]
    public async Task UploadDocument_SameClientRefTwice_RecordsOnce()
    {
        var client = NewClient();

        await client.UploadDocument(SeededVisa, Document(), "REF-1", CancellationToken.None);
        await client.UploadDocument(SeededVisa, Document(), "REF-1", CancellationToken.None);

        Assert.Single(client.RecordedDocuments);
    }

    [Fact]
    public async Task RecordArrival_SameClientRefTwice_RecordsOnce()
    {
        var client = NewClient();

        await client.RecordArrival(SeededVisa, Arrival(), "REF-1", CancellationToken.None);
        await client.RecordArrival(SeededVisa, Arrival(), "REF-1", CancellationToken.None);

        Assert.Single(client.RecordedArrivals);
    }

    [Fact]
    public async Task RecordArrival_DistinctClientRefs_RecordsEach()
    {
        var client = NewClient();

        await client.RecordArrival(SeededVisa, Arrival(), "REF-1", CancellationToken.None);
        await client.RecordArrival(SeededVisa, Arrival(), "REF-2", CancellationToken.None);

        Assert.Equal(2, client.RecordedArrivals.Count);
    }

    [Fact]
    public async Task FailedPush_DoesNotConsumeClientRef()
    {
        // The rule the outbox depends on: a push that failed must still be retryable under the same
        // clientRef. If a failure consumed the ref, every transient error would silently become a
        // permanently lost document — the exact failure this project exists to eliminate.
        var (behavior, options, _) = FakeTestHarness.Build(failureRate: 1);
        var client = new FakeNext3Client(behavior);

        await Assert.ThrowsAsync<FakeTransientException>(() =>
            client.UploadDocument(SeededVisa, Document(), "REF-1", CancellationToken.None));
        Assert.Empty(client.RecordedDocuments);

        options.CurrentValue = new FakeOptions { FailureRate = 0 };
        await client.UploadDocument(SeededVisa, Document(), "REF-1", CancellationToken.None);

        Assert.Single(client.RecordedDocuments);
    }

    [Fact]
    public async Task ReplayOfSentPush_SucceedsEvenWhileFailureInjectionIsOn()
    {
        // Dedupe is checked before the failure roll, so a retry of something NEXT3 already accepted
        // cannot be turned into a failure by an unrelated outage.
        var (behavior, options, _) = FakeTestHarness.Build();
        var client = new FakeNext3Client(behavior);
        await client.UploadDocument(SeededVisa, Document(), "REF-1", CancellationToken.None);

        options.CurrentValue = new FakeOptions { FailureRate = 1 };
        await client.UploadDocument(SeededVisa, Document(), "REF-1", CancellationToken.None);

        Assert.Single(client.RecordedDocuments);
    }

    [Fact]
    public async Task GetClaim_ReturnsSeededClaim_AndNullForUnknownVisa()
    {
        var client = NewClient();

        var found = await client.GetClaim(SeededVisa, CancellationToken.None);
        var missing = await client.GetClaim("PLACEHOLDER-VISA-NOPE", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(SeededVisa, found.VisaNo);
        Assert.Null(missing);
    }

    [Fact]
    public async Task SearchClaims_MatchesByPlateAndByVisa_AndReturnsNothingWithoutTerms()
    {
        var client = NewClient();

        var byPlate = await client.SearchClaims("PLC-TEST-02", null, CancellationToken.None);
        var byVisa = await client.SearchClaims(null, "PLACEHOLDER-VISA-0003", CancellationToken.None);
        var neither = await client.SearchClaims(null, null, CancellationToken.None);

        Assert.Equal("PLACEHOLDER-VISA-0002", Assert.Single(byPlate).VisaNo);
        Assert.Equal("PLC-TEST-03", Assert.Single(byVisa).PlateNo);
        Assert.Empty(neither);
    }

    [Fact]
    public async Task GetExperts_ReturnsSeeds_IncludingAnInactiveOne()
    {
        var client = NewClient();

        var experts = await client.GetExperts(CancellationToken.None);

        Assert.Equal(3, experts.Count);
        Assert.Contains(experts, e => !e.Active);
    }

    [Fact]
    public async Task PushToUnknownVisa_ThrowsNonTransient()
    {
        // Not a FakeTransientException: retrying will never make an unknown visa exist, so the
        // outbox must be able to tell this apart and drive the row to `failed` (slice 2.2).
        var client = NewClient();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.UploadDocument("PLACEHOLDER-VISA-NOPE", Document(), "REF-1", CancellationToken.None));

        Assert.IsNotType<FakeTransientException>(ex);
    }

    [Fact]
    public async Task ConfiguredLatency_DelaysCompletion()
    {
        var (behavior, _, time) = FakeTestHarness.Build(latencyMs: 5_000);
        var client = new FakeNext3Client(behavior);

        var pending = client.GetClaim(SeededVisa, CancellationToken.None);
        Assert.False(pending.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(5));

        Assert.NotNull(await pending);
    }

    private static FakeNext3Client NewClient() => new(FakeTestHarness.Build().Behavior);

    private static ArrivalInfo Arrival() =>
        new(new DateTimeOffset(2026, 8, 19, 9, 30, 0, TimeSpan.Zero), 25.2048, 55.2708);

    private static DocumentPush Document() =>
        new(Next3Folders.ExpertDocuments, "PLACEHOLDER-DOC-01", "photo.jpg", "image/jpeg", "blob/photo.jpg");
}
