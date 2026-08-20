using Api.Integrations.Next3;
using Api.Modules.Expert;
using Api.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// The rule design.md §4 and CLAUDE.md both state: the domain row and its outbox row commit in one
/// transaction. If they can't, you get either documents that are never pushed or pushes for documents
/// that don't exist — and both surface weeks later as "AXA is missing photos", which is the exact
/// problem this project exists to solve.
///
/// The DoD names the `document` row, but that table is slice 2.3's. This proves the same guarantee
/// with the pairing that exists today: §5.1's Arrived write (`expert_assignment.arrived_at`, columns
/// created in 2.1) alongside its `update_arrival` row. 2.3 re-asserts it with the document row.
/// </summary>
[Collection("api")]
public sealed class OutboxAtomicityTests(ApiFixture fixture)
{
    private static readonly ArrivalInfo Arrival =
        new(new DateOnly(2026, 8, 20), new TimeOnly(9, 30), 25.2048, 55.2708);

    [Fact]
    public async Task TheDomainWriteAndTheOutboxRow_CommitTogether()
    {
        var assignment = await ArrangeAssignment();
        var clientRef = OutboxFlows.NextClientRef();

        await using (var db = fixture.CreateDbContext())
        {
            var row = await db.ExpertAssignments.SingleAsync(a => a.Id == assignment.Id);
            row.ArrivedAt = fixture.Time.GetUtcNow().UtcDateTime;
            row.ArrivalLat = Arrival.Latitude;
            row.ArrivalLng = Arrival.Longitude;

            new OutboxWriter(db, fixture.Time).EnqueueArrival(row.VisaNo, Arrival, clientRef);

            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDbContext())
        {
            var row = await db.ExpertAssignments.AsNoTracking().SingleAsync(a => a.Id == assignment.Id);
            Assert.NotNull(row.ArrivedAt);

            var message = await db.Set<Next3OutboxMessage>().AsNoTracking()
                .SingleAsync(m => m.VisaNo == assignment.VisaNo);
            Assert.Equal(Next3OutboxOperations.UpdateArrival, message.Operation);
            Assert.Equal(Next3OutboxStatuses.Pending, message.Status);
            Assert.Equal(0, message.Attempts);
            // Due immediately, or the first pass after an expert presses Arrived would do nothing.
            Assert.Equal(message.CreatedAt, message.NextRetryAt);
            Assert.Contains(clientRef, message.Payload, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task WhenTheCommitFails_NeitherRowSurvives()
    {
        // This is the test that goes red the day someone "helpfully" adds a SaveChanges inside
        // OutboxWriter.Enqueue. The writer adds; only the caller commits.
        var assignment = await ArrangeAssignment();

        await using (var db = fixture.CreateDbContext())
        {
            var row = await db.ExpertAssignments.SingleAsync(a => a.Id == assignment.Id);
            row.ArrivedAt = fixture.Time.GetUtcNow().UtcDateTime;

            new OutboxWriter(db, fixture.Time)
                .EnqueueArrival(row.VisaNo, Arrival, OutboxFlows.NextClientRef());

            // A second row that violates CK_next3_outbox_operation makes SaveChanges fail at the
            // database rather than in EF's change tracker, so SQL Server rolls the whole batch back —
            // the realistic version of "the transaction did not commit". It also proves the check
            // constraint is really on the table.
            db.Set<Next3OutboxMessage>().Add(new Next3OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                VisaNo = row.VisaNo,
                Operation = "PLACEHOLDER-not-an-operation",
                Payload = "{}",
                Status = Next3OutboxStatuses.Pending,
                NextRetryAt = fixture.Time.GetUtcNow().UtcDateTime,
                CreatedAt = fixture.Time.GetUtcNow().UtcDateTime,
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await using (var verify = fixture.CreateDbContext())
        {
            var row = await verify.ExpertAssignments.AsNoTracking().SingleAsync(a => a.Id == assignment.Id);
            Assert.Null(row.ArrivedAt);

            Assert.Empty(await verify.Set<Next3OutboxMessage>().AsNoTracking()
                .Where(m => m.VisaNo == assignment.VisaNo).ToListAsync());
        }
    }

    /// <summary>An assignment to attach an arrival to, delivered through the real 2.1 path.</summary>
    private async Task<ExpertAssignment> ArrangeAssignment()
    {
        using var expert = await fixture.CreateMappedExpert();
        var visa = fixture.SeedClaim();
        await fixture.Inject(visa, expert.Next3Id, ExpertFlows.NextRef());

        await using var db = fixture.CreateDbContext();
        return await db.ExpertAssignments.AsNoTracking().SingleAsync(a => a.VisaNo == visa);
    }
}
