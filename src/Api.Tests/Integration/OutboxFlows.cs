using Api.Infrastructure;
using Api.Integrations;
using Api.Integrations.Next3;
using Api.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests.Integration;

internal static class OutboxFlows
{
    private static int _counter;

    /// <summary>A clientRef no other test uses — the whole suite shares one database for the run.</summary>
    public static string NextClientRef() =>
        $"PLACEHOLDER-REF-T{Interlocked.Increment(ref _counter):D5}";

    /// <summary>A document push whose values are obviously fake (Appendix A's grep rule).</summary>
    public static DocumentPush TestDocument(string? blobKey = null) => new(
        Next3Folders.ExpertDocuments,
        "PLACEHOLDER-DOC-01",
        "PLACEHOLDER-photo.jpg",
        "image/jpeg",
        blobKey ?? $"PLACEHOLDER/blob/{Guid.CreateVersion7():N}");

    /// <summary>
    /// Clears the queue, then enqueues one document — the arrange step for any test that runs the
    /// processor. Clearing matters because the dequeue claims a batch across the whole table and
    /// cannot be scoped to one test's rows: a batch filled with another test's leftovers would leave
    /// this test's row unclaimed and its assertions failing for reasons that have nothing to do with
    /// the outbox.
    /// </summary>
    public static async Task<Guid> QueueOneDocument(
        this ApiFixture fixture, string visaNo, string clientRef, DocumentPush? doc = null)
    {
        await fixture.ClearQueue();
        return await fixture.EnqueueDocument(visaNo, clientRef, doc);
    }

    /// <summary>
    /// Queues a document push the way a producer will (slice 2.3): through the writer, committed by
    /// the caller's SaveChanges.
    /// </summary>
    public static async Task<Guid> EnqueueDocument(
        this ApiFixture fixture, string visaNo, string clientRef, DocumentPush? doc = null)
    {
        await using var db = fixture.CreateDbContext();
        var writer = new OutboxWriter(db, fixture.Time);
        var messageId = writer.EnqueueDocument(visaNo, doc ?? TestDocument(), clientRef);
        await db.SaveChangesAsync();
        return messageId;
    }

    public static async Task<Next3OutboxMessage> Row(this ApiFixture fixture, Guid messageId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<Next3OutboxMessage>().AsNoTracking().SingleAsync(m => m.Id == messageId);
    }

    /// <summary>Makes a row due again without moving the shared clock (see the backoff walk).</summary>
    public static async Task MakeDue(this ApiFixture fixture, Guid messageId)
    {
        await using var db = fixture.CreateDbContext();
        var now = fixture.Time.GetUtcNow().UtcDateTime;
        await db.Database.ExecuteSqlAsync(
            $"UPDATE next3_outbox SET next_retry_at = {now} WHERE id = {messageId}");
    }

    /// <summary>The fake NEXT3 the worker actually pushes through (a singleton, shared with it).</summary>
    public static FakeNext3Client FakeNext3(this ApiFixture fixture) =>
        fixture.Services.GetRequiredService<FakeNext3Client>();

    public static int DocumentsRecordedFor(this ApiFixture fixture, string visaNo) =>
        fixture.FakeNext3().RecordedDocuments.Count(d => d.VisaNo == visaNo);

    /// <summary>The arrival side of <see cref="DocumentsRecordedFor"/> (slice 2.4's Arrived button).</summary>
    public static IReadOnlyList<(string VisaNo, ArrivalInfo Info, string ClientRef)> ArrivalsRecordedFor(
        this ApiFixture fixture, string visaNo) =>
        [.. fixture.FakeNext3().RecordedArrivals.Where(a => a.VisaNo == visaNo)];

    /// <summary>
    /// Runs a scenario with a modified outbox configuration, then restores it. The monitor is shared
    /// by the whole serialized collection, so a leaked value breaks later tests — the same discipline
    /// <see cref="ExpertFlows.WithNext3Down{T}"/> applies to the failure rate.
    /// </summary>
    public static async Task WithOutboxOptions(
        this ApiFixture fixture, Action<OutboxOptions> configure, Func<Task> scenario)
    {
        var original = fixture.Outbox.CurrentValue;
        var patched = Clone(original);
        configure(patched);
        fixture.Outbox.CurrentValue = patched;
        try
        {
            await scenario();
        }
        finally
        {
            fixture.Outbox.CurrentValue = original;
        }
    }

    /// <summary>Runs a scenario with NEXT3 failing every call, then restores the failure rate.</summary>
    public static async Task WithNext3Down(this ApiFixture fixture, Func<Task> scenario)
    {
        var original = fixture.Fake.CurrentValue;
        fixture.Fake.CurrentValue = new FakeOptions { FailureRate = 1, LatencyMs = original.LatencyMs };
        try
        {
            await scenario();
        }
        finally
        {
            fixture.Fake.CurrentValue = original;
        }
    }

    /// <summary>
    /// Marks every outstanding row `sent` so a test that cannot scope the dequeue to its own rows
    /// starts from an empty queue. Safe only because the integration classes are one serialized
    /// xUnit collection — nothing else is mid-flight when this runs.
    /// </summary>
    public static async Task ClearQueue(this ApiFixture fixture)
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE next3_outbox SET status = 'sent', sent_at = SYSUTCDATETIME() "
            + "WHERE status IN ('pending', 'processing')");
    }

    private static OutboxOptions Clone(OutboxOptions source) => new()
    {
        MaxAttempts = source.MaxAttempts,
        BackoffCeilingHours = source.BackoffCeilingHours,
        BatchSize = source.BatchSize,
        PollSeconds = source.PollSeconds,
        LeaseSeconds = source.LeaseSeconds,
        WorkerEnabled = source.WorkerEnabled,
    };
}
