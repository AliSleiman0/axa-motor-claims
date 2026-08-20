using Api.Outbox;

namespace Api.Tests.Integration;

/// <summary>
/// The first of design.md §6.3's "two classic bugs, designed out": a retry after a timeout that
/// actually succeeded must not duplicate the document in NEXT3. Every push carries a `clientRef` that
/// is stable across retries, and the fake consumes one only on success (slice 1.4's load-bearing
/// ordering). #32 asks whether the real NEXT3 dedupes the same way; until it answers, RealNext3Client
/// will keep its own sent-log check as well (slice 3.3).
/// </summary>
[Collection("api")]
public sealed class OutboxIdempotencyTests(ApiFixture fixture)
{
    [Fact]
    public async Task ARetryOfAPushThatAlreadySucceeded_DoesNotDuplicate()
    {
        // The dangerous case is a push NEXT3 accepted but whose response we never saw — the row stays
        // `pending` and gets pushed again. The fake cannot inject "succeed, then fail on the way
        // back", so the accepted push is made directly here and the outbox row then carries the same
        // clientRef. What is being asserted is the same thing either way: the second delivery attempt
        // under an already-consumed ref adds nothing.
        var visa = fixture.SeedClaim();
        var clientRef = OutboxFlows.NextClientRef();
        var doc = OutboxFlows.TestDocument();

        await fixture.FakeNext3().UploadDocument(visa, doc, clientRef, default);
        Assert.Equal(1, fixture.DocumentsRecordedFor(visa));

        var messageId = await fixture.QueueOneDocument(visa, clientRef, doc);
        await fixture.OutboxProcessor.RunOnce(default);

        var row = await fixture.Row(messageId);
        Assert.Equal(Next3OutboxStatuses.Sent, row.Status);
        Assert.NotNull(row.SentAt);

        // Still one. A duplicate here is a photo filed twice under a visa — the manual mess this
        // project replaces, recreated automatically.
        Assert.Equal(1, fixture.DocumentsRecordedFor(visa));
    }

    [Fact]
    public async Task AFailedPush_DoesNotConsumeTheClientRef()
    {
        // The other half of the ordering: if a failed push burned its ref, the retry would be
        // deduplicated away and the document would be lost forever while the row read `sent`.
        var visa = fixture.SeedClaim();
        var clientRef = OutboxFlows.NextClientRef();
        var messageId = await fixture.QueueOneDocument(visa, clientRef);

        await fixture.WithNext3Down(async () => await fixture.OutboxProcessor.RunOnce(default));
        Assert.Equal(0, fixture.DocumentsRecordedFor(visa));

        await fixture.MakeDue(messageId);
        await fixture.OutboxProcessor.RunOnce(default);

        Assert.Equal(Next3OutboxStatuses.Sent, (await fixture.Row(messageId)).Status);
        Assert.Equal(1, fixture.DocumentsRecordedFor(visa));
    }

    [Fact]
    public async Task TheClientRefSurvivesEveryRetryUnchanged()
    {
        // Idempotency only works if the ref is stable. It is written into the payload once, at
        // enqueue time, and nothing on the retry path rewrites it.
        var visa = fixture.SeedClaim();
        var clientRef = OutboxFlows.NextClientRef();
        var messageId = await fixture.QueueOneDocument(visa, clientRef);

        var payloads = new List<string>();
        await fixture.WithNext3Down(async () =>
        {
            for (var i = 0; i < 3; i++)
            {
                await fixture.OutboxProcessor.RunOnce(default);
                payloads.Add((await fixture.Row(messageId)).Payload);
                await fixture.MakeDue(messageId);
            }
        });

        Assert.All(payloads, p => Assert.Contains(clientRef, p, StringComparison.Ordinal));
        Assert.Single(payloads.Distinct(StringComparer.Ordinal));
    }
}
