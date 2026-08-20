using Api.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §6.3's retry behaviour end to end: with NEXT3 failing, rows retry on schedule and land
/// `sent` or `failed`. This is the DoD sentence for slice 2.2, and the week-4 demo's "kill NEXT3
/// mid-flow, show the queue drain on recovery" beat written as tests.
///
/// Every test that raises the failure rate restores it in a finally: one FakeBehavior is shared by
/// NEXT3 and all three senders, and the integration classes run as one serialized collection.
/// </summary>
[Collection("api")]
public sealed class OutboxRetryTests(ApiFixture fixture)
{
    [Fact]
    public async Task WhenThePushFails_TheRowGoesBackToPendingWithTheFirstBackoff()
    {
        var visa = fixture.SeedClaim();
        var messageId = await fixture.QueueOneDocument(visa, OutboxFlows.NextClientRef());
        var before = fixture.Time.GetUtcNow().UtcDateTime;

        await fixture.WithNext3Down(async () => await fixture.OutboxProcessor.RunOnce(default));

        var row = await fixture.Row(messageId);
        Assert.Equal(Next3OutboxStatuses.Pending, row.Status);
        // Incremented by the dequeue, not the worker — a worker that dies after claiming still counts.
        Assert.Equal(1, row.Attempts);
        Assert.Null(row.SentAt);
        Assert.NotNull(row.LastError);
        Assert.Equal(before + TimeSpan.FromMinutes(1), row.NextRetryAt);
    }

    [Fact]
    public async Task ARowThatIsNotYetDue_IsNotDequeuedAgain()
    {
        var visa = fixture.SeedClaim();
        var messageId = await fixture.QueueOneDocument(visa, OutboxFlows.NextClientRef());

        await fixture.WithNext3Down(async () =>
        {
            await fixture.OutboxProcessor.RunOnce(default);
            // Second pass at the same instant: next_retry_at is a minute out, so nothing to claim.
            await fixture.OutboxProcessor.RunOnce(default);
        });

        Assert.Equal(1, (await fixture.Row(messageId)).Attempts);
    }

    [Fact]
    public async Task AClockAdvancePastNextRetry_MakesTheRowDueAgain()
    {
        // The one test that moves the shared clock. It is what proves the worker's own time — not
        // SYSUTCDATETIME() inside the procedure — decides eligibility.
        var visa = fixture.SeedClaim();
        var messageId = await fixture.QueueOneDocument(visa, OutboxFlows.NextClientRef());

        await fixture.WithNext3Down(async () =>
        {
            await fixture.OutboxProcessor.RunOnce(default);
            fixture.Time.Advance(TimeSpan.FromMinutes(1));
            await fixture.OutboxProcessor.RunOnce(default);
        });

        Assert.Equal(2, (await fixture.Row(messageId)).Attempts);
    }

    [Fact]
    public async Task RetriesFollowTheBackoffScheduleAndThenFail()
    {
        // Walks the whole §6.3 schedule, asserting next_retry_at by value and then making the row due
        // again in the database rather than advancing the shared clock eight times.
        TimeSpan[] expected =
        [
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(2),
            TimeSpan.FromHours(6),
            TimeSpan.FromHours(6),
            TimeSpan.FromHours(6),
        ];

        var visa = fixture.SeedClaim();
        var messageId = await fixture.QueueOneDocument(visa, OutboxFlows.NextClientRef());
        var now = fixture.Time.GetUtcNow().UtcDateTime;

        await fixture.WithNext3Down(async () =>
        {
            for (var attempt = 1; attempt <= expected.Length; attempt++)
            {
                await fixture.OutboxProcessor.RunOnce(default);

                var row = await fixture.Row(messageId);
                Assert.Equal(attempt, row.Attempts);
                Assert.Equal(Next3OutboxStatuses.Pending, row.Status);
                Assert.Equal(now + expected[attempt - 1], row.NextRetryAt);

                await fixture.MakeDue(messageId);
            }

            // The 8th failure is terminal: §6.3's "after ~8 attempts → failed".
            await fixture.OutboxProcessor.RunOnce(default);
        });

        var failed = await fixture.Row(messageId);
        Assert.Equal(8, failed.Attempts);
        Assert.Equal(Next3OutboxStatuses.Failed, failed.Status);
        Assert.Null(failed.SentAt);
        Assert.NotNull(failed.LastError);
    }

    [Fact]
    public async Task WhenNext3Recovers_TheQueueDrainsToSent()
    {
        // The outbox's whole sales pitch, and the week-4 demo: NEXT3 goes down, nothing is lost, the
        // queue drains on recovery and nobody notices.
        var visa = fixture.SeedClaim();
        var clientRef = OutboxFlows.NextClientRef();
        var messageId = await fixture.QueueOneDocument(visa, clientRef);

        await fixture.WithNext3Down(async () =>
        {
            await fixture.OutboxProcessor.RunOnce(default);
            await fixture.MakeDue(messageId);
            await fixture.OutboxProcessor.RunOnce(default);
        });

        Assert.Equal(Next3OutboxStatuses.Pending, (await fixture.Row(messageId)).Status);
        Assert.Equal(0, fixture.DocumentsRecordedFor(visa));

        await fixture.MakeDue(messageId);
        await fixture.OutboxProcessor.RunOnce(default);

        var row = await fixture.Row(messageId);
        Assert.Equal(Next3OutboxStatuses.Sent, row.Status);
        Assert.Equal(fixture.Time.GetUtcNow().UtcDateTime, row.SentAt);
        Assert.Equal(3, row.Attempts);
        Assert.Null(row.LastError);

        // Two failures earlier did not consume the clientRef, so the document really did arrive.
        Assert.Equal(1, fixture.DocumentsRecordedFor(visa));
        Assert.Contains(
            fixture.FakeNext3().RecordedDocuments,
            d => d.VisaNo == visa && d.ClientRef == clientRef);
    }

    [Fact]
    public async Task APermanentFailure_GoesStraightToFailedWithoutRetrying()
    {
        // The fake throws InvalidOperationException for a visa NEXT3 does not know, deliberately not
        // FakeTransientException (slice 1.4). Retrying that eight times over fourteen hours would
        // hide a problem only a human can fix, so it lands on A2 immediately instead.
        var messageId = await fixture.QueueOneDocument(
            ExpertFlows.NextVisa(), OutboxFlows.NextClientRef());

        await fixture.OutboxProcessor.RunOnce(default);

        var row = await fixture.Row(messageId);
        Assert.Equal(Next3OutboxStatuses.Failed, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.Null(row.SentAt);
        Assert.Contains(nameof(InvalidOperationException), row.LastError!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStrandedProcessingRow_IsReclaimedAfterItsLease()
    {
        // A worker that dies between claiming a row and writing its outcome. Without the lease the
        // row sits in `processing` forever: never retried, and absent from A2's `failed` list.
        var visa = fixture.SeedClaim();
        var messageId = await fixture.QueueOneDocument(visa, OutboxFlows.NextClientRef());

        await using (var db = fixture.CreateDbContext())
        {
            var stranded = fixture.Time.GetUtcNow().UtcDateTime - TimeSpan.FromHours(1);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE next3_outbox SET status = 'processing', attempts = 1, next_retry_at = {stranded} WHERE id = {messageId}");
        }

        await fixture.OutboxProcessor.RunOnce(default);

        var row = await fixture.Row(messageId);
        Assert.Equal(Next3OutboxStatuses.Sent, row.Status);
        Assert.Equal(2, row.Attempts);
    }

    [Fact]
    public async Task APoisonRowDoesNotRollBackTheRestOfItsBatch()
    {
        // One SaveChanges per message, not per batch. A row that cannot push must not take the
        // outcomes of the rows beside it down with it.
        var goodVisa = fixture.SeedClaim();
        var goodId = await fixture.QueueOneDocument(goodVisa, OutboxFlows.NextClientRef());
        var poisonId = await fixture.EnqueueDocument(
            ExpertFlows.NextVisa(), OutboxFlows.NextClientRef());

        await fixture.OutboxProcessor.RunOnce(default);

        Assert.Equal(Next3OutboxStatuses.Sent, (await fixture.Row(goodId)).Status);
        Assert.Equal(Next3OutboxStatuses.Failed, (await fixture.Row(poisonId)).Status);
    }
}
