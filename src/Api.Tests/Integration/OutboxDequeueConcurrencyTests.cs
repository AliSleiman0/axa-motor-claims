using Api.Outbox;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §6.3's dequeue under two workers. READPAST is SQL Server's SKIP LOCKED: a second worker
/// steps over rows the first has claimed instead of blocking behind them.
///
/// The claim is one atomic UPDATE ... OUTPUT, which is the point — reading `status` and then writing
/// it would be two steps that two workers both pass. That is the trap that bit `public_link_token` in
/// slice 1.5, and the answer is the same: the guard lives in the schema.
///
/// Both tests clear the queue first. The dequeue claims a batch across the whole table and cannot be
/// scoped to one test's rows; clearing is safe only because the integration classes are one
/// serialized xUnit collection, so nothing else is in flight.
/// </summary>
[Collection("api")]
public sealed class OutboxDequeueConcurrencyTests(ApiFixture fixture)
{
    private const int BatchSize = 10;

    [Fact]
    public async Task TwoConcurrentWorkers_NeverProcessTheSameRow()
    {
        var visa = fixture.SeedClaim();
        await fixture.ClearQueue();

        var ids = new List<Guid>();
        for (var i = 0; i < BatchSize * 2; i++)
        {
            ids.Add(await fixture.EnqueueDocument(visa, OutboxFlows.NextClientRef()));
        }

        // Asserted on the outcome, not on which worker won — the house rule from the 1.5 concurrent
        // submit test. Either split is correct; a row pushed twice is not.
        await Task.WhenAll(
            fixture.OutboxProcessor.RunOnce(default),
            fixture.OutboxProcessor.RunOnce(default));

        await using var db = fixture.CreateDbContext();
        var rows = await db.Set<Next3OutboxMessage>().AsNoTracking()
            .Where(m => ids.Contains(m.Id)).ToListAsync();

        Assert.Equal(ids.Count, rows.Count);
        Assert.All(rows, r => Assert.Equal(Next3OutboxStatuses.Sent, r.Status));
        // Exactly one attempt each: a row claimed by both workers would show two.
        Assert.All(rows, r => Assert.Equal(1, r.Attempts));

        // And the same count arrived at NEXT3 — no duplicates, none lost.
        Assert.Equal(ids.Count, fixture.DocumentsRecordedFor(visa));
    }

    [Fact]
    public async Task AClaimedBatchDoesNotBlockASecondWorker()
    {
        // The test that actually proves READPAST is present. Connection A claims a batch inside an
        // open transaction and holds the locks; connection B must claim the *other* rows immediately.
        // Drop READPAST from the procedure and B blocks on A's locks until its command timeout, so
        // this fails with a timeout rather than with a wrong row count.
        var visa = fixture.SeedClaim();
        await fixture.ClearQueue();

        for (var i = 0; i < BatchSize * 2; i++)
        {
            await fixture.EnqueueDocument(visa, OutboxFlows.NextClientRef());
        }

        var now = fixture.Time.GetUtcNow().UtcDateTime;

        await using var holder = new SqlConnection(fixture.ConnectionString);
        await holder.OpenAsync();
        await using var tx = (SqlTransaction)await holder.BeginTransactionAsync();

        var held = await ClaimIds(holder, tx, now, commandTimeoutSeconds: 30);
        Assert.Equal(BatchSize, held.Count);

        await using (var second = new SqlConnection(fixture.ConnectionString))
        {
            await second.OpenAsync();
            var other = await ClaimIds(second, transaction: null, now, commandTimeoutSeconds: 10);

            Assert.Equal(BatchSize, other.Count);
            Assert.Empty(other.Intersect(held));
        }

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task AReclaimedRow_RejectsTheOriginalWorkersOutcome()
    {
        // The lease means an expired claim is handed to another worker — but the first worker may
        // still be alive and about to write its outcome. Without a guard the slow one wins by
        // arriving last: a row already `sent` gets rewritten to `failed`, or a `failed` row gets a
        // `sent_at` it never earned. §7.3 deletes blobs on `sent` and A2 lists `failed`, so both the
        // retention rule and the safety net would act on a status NEXT3 never agreed to.
        //
        // `attempts` is the concurrency token: a reclaim increments it, so the stale write finds no
        // matching row. Drop IsConcurrencyToken() from the configuration and this goes green-to-red.
        var visa = fixture.SeedClaim();
        await fixture.ClearQueue();
        var messageId = await fixture.EnqueueDocument(visa, OutboxFlows.NextClientRef());

        await using var db = fixture.CreateDbContext();
        var now = fixture.Time.GetUtcNow().UtcDateTime;

        // Worker A claims the row.
        var claimed = await new OutboxDequeue(db).Claim(BatchSize, now, leaseSeconds: 300, maxAttempts: 8, default);
        var mine = Assert.Single(claimed, m => m.Id == messageId);

        // Worker B reclaims it after the lease expires, from its own connection.
        await using (var other = fixture.CreateDbContext())
        {
            var later = now + TimeSpan.FromHours(1);
            var reclaimed = await new OutboxDequeue(other)
                .Claim(BatchSize, later, leaseSeconds: 300, maxAttempts: 8, default);
            Assert.Contains(reclaimed, m => m.Id == messageId);
        }

        // Worker A now finishes and tries to record its outcome. It must lose.
        mine.Status = Next3OutboxStatuses.Sent;
        mine.SentAt = now;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());

        var row = await fixture.Row(messageId);
        Assert.Equal(Next3OutboxStatuses.Processing, row.Status);
        Assert.Null(row.SentAt);
        Assert.Equal(2, row.Attempts);
    }

    [Fact]
    public async Task ARowThatKeepsOutlivingItsWorker_IsRetiredInsteadOfReclaimedForever()
    {
        // The give-up rule lives in application code, which only runs when a worker survives long
        // enough to record an outcome. A row that reliably kills its worker would climb past
        // MaxAttempts and be re-pushed every lease period, invisible to A2 the whole time — the lease
        // would have relocated the stranding it exists to prevent rather than removing it.
        var visa = fixture.SeedClaim();
        await fixture.ClearQueue();
        var messageId = await fixture.EnqueueDocument(visa, OutboxFlows.NextClientRef());

        await using (var db = fixture.CreateDbContext())
        {
            var expired = fixture.Time.GetUtcNow().UtcDateTime - TimeSpan.FromHours(1);
            await db.Database.ExecuteSqlAsync(
                $"UPDATE next3_outbox SET status = 'processing', attempts = 8, next_retry_at = {expired} WHERE id = {messageId}");
        }

        await fixture.OutboxProcessor.RunOnce(default);

        var row = await fixture.Row(messageId);
        Assert.Equal(Next3OutboxStatuses.Failed, row.Status);
        Assert.Equal(8, row.Attempts);
        Assert.Null(row.SentAt);
        Assert.NotNull(row.LastError);
    }

    [Fact]
    public async Task TheOutboxTableCarriesNoTrigger()
    {
        // §6.3's dequeue returns OUTPUT inserted.*, and EF abandons the OUTPUT clause on any table it
        // knows carries a trigger — so a future slice adding an append-only trigger the way audit_log
        // got one in 1.3 would break the dequeue at runtime. Three code comments said so; this is the
        // one that will actually stop it.
        await using var db = fixture.CreateDbContext();
        var triggers = await db.Database
            .SqlQuery<string>($"SELECT name AS Value FROM sys.triggers WHERE parent_id = OBJECT_ID('dbo.next3_outbox')")
            .ToListAsync();

        Assert.Empty(triggers);
    }

    private static async Task<List<Guid>> ClaimIds(
        SqlConnection connection, SqlTransaction? transaction, DateTime now, int commandTimeoutSeconds)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = commandTimeoutSeconds;
        command.CommandText =
            $"EXEC {OutboxDequeue.ProcedureName} @batch_size = @b, @now = @n, @lease_seconds = @l";
        command.Parameters.AddWithValue("@b", BatchSize);
        command.Parameters.AddWithValue("@n", now);
        command.Parameters.AddWithValue("@l", 300);

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(reader.GetOrdinal("id")));
        }

        return ids;
    }
}
