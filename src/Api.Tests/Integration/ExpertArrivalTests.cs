using System.Net;
using System.Net.Http.Json;
using Api.Modules.Audit;
using Api.Modules.Users;
using Api.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §5.1's Arrived row: "arrived_at, arrival_lat/lng on the assignment; outbox row
/// update_arrival (date, time, GPS); audit_log. Button disabled after first press; geolocation-denied
/// shows a blocking explanation (arrival without location is not sent — the BRD requires all three
/// values)."
///
/// The denial itself is a browser fact and is covered in the web suite; what is pinned here is the
/// rule behind it — the server refuses an arrival with no coordinates, so a client that skipped the
/// check cannot push a half-arrival into NEXT3.
/// </summary>
[Collection("api")]
public sealed class ExpertArrivalTests(ApiFixture fixture)
{
    [Fact]
    public async Task PressingArrived_StampsTheAssignment_QueuesUpdateArrival_AndAudits()
    {
        var (expert, assignmentId, visa) = await Assigned();
        var now = fixture.Time.GetUtcNow().UtcDateTime;

        var response = await ExpertFlows.PressArrived(expert.Client, assignmentId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ArrivalBodyDto>();
        Assert.Equal(now, body!.ArrivedAt);
        Assert.Equal(ExpertFlows.TestLatitude, body.Latitude);
        Assert.Equal(ExpertFlows.TestLongitude, body.Longitude);

        await using var db = fixture.CreateDbContext();
        var assignment = await db.ExpertAssignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId);
        // The server's clock, not the caller's: the request carries coordinates and nothing else, so
        // an expert's phone cannot decide when it arrived.
        Assert.Equal(now, assignment.ArrivedAt);
        Assert.Equal(ExpertFlows.TestLatitude, assignment.ArrivalLat);
        Assert.Equal(ExpertFlows.TestLongitude, assignment.ArrivalLng);

        var message = await db.Set<Next3OutboxMessage>().AsNoTracking()
            .SingleAsync(m => m.VisaNo == visa && m.Operation == Next3OutboxOperations.UpdateArrival);
        Assert.Equal(Next3OutboxStatuses.Pending, message.Status);
        Assert.Equal(0, message.Attempts);
        // Due immediately, or the first worker pass after the press would do nothing.
        Assert.Equal(message.CreatedAt, message.NextRetryAt);
        // clientRef = the assignment id (§6.3: stable across retries).
        Assert.Contains(assignmentId.ToString(), message.Payload, StringComparison.Ordinal);

        // §9: "Arrived presses with coordinates".
        var audit = await db.Set<AuditLog>().AsNoTracking().SingleAsync(a =>
            a.Action == AuditActions.AssignmentArrived && a.EntityId == assignmentId);
        Assert.Equal(expert.User.Id, audit.ActorUserId);
        Assert.Equal(AuditEntityKinds.ExpertAssignment, audit.EntityKind);
        Assert.Contains(visa, audit.Detail, StringComparison.Ordinal);
        Assert.Contains("25.2048", audit.Detail, StringComparison.Ordinal);
        expert.Dispose();
    }

    [Fact]
    public async Task SecondPress_ChangesNothing_AndQueuesNoSecondPush()
    {
        // §5.1 disables the button after the first press, so a second request is a double tap or a
        // replay. It reports the arrival that happened rather than an error for something that
        // worked — but it must not stamp a new time, and above all must not tell NEXT3 twice.
        var (expert, assignmentId, visa) = await Assigned();
        var first = await ExpertFlows.PressArrived(expert.Client, assignmentId);
        var arrivedAt = (await first.Content.ReadFromJsonAsync<ArrivalBodyDto>())!.ArrivedAt;

        // Under Auth.Jwt.AccessTokenMinutes (15) with ClockSkew zero — travelling further would
        // expire this client's access token and the assertion would fail as a 401.
        fixture.Time.Advance(TimeSpan.FromMinutes(10));
        var second = await ExpertFlows.PressArrived(expert.Client, assignmentId);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<ArrivalBodyDto>();
        Assert.Equal(arrivedAt, body!.ArrivedAt);
        Assert.Equal(ExpertFlows.TestLatitude, body.Latitude);

        await AssertExactlyOneArrival(assignmentId, visa);
        expert.Dispose();
    }

    [Fact]
    public async Task ConcurrentPresses_ProduceExactlyOneArrival()
    {
        // The 1.5 lesson, applied: a read-then-write on a state column is not a state machine. Every
        // press here passes the "not arrived yet" read; only the WHERE clause on the UPDATE settles
        // it. Remove `&& a.ArrivedAt == null` from that query and this goes red.
        var (expert, assignmentId, visa) = await Assigned();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => ExpertFlows.PressArrived(expert.Client, assignmentId)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await AssertExactlyOneArrival(assignmentId, visa);

        // Every response must describe the arrival that was actually stored, not the caller's own
        // attempt: a loser that reported its own timestamp would be indistinguishable from a winner
        // here, since all four share the frozen clock — but not on a real one.
        await using var db = fixture.CreateDbContext();
        var stored = (await db.ExpertAssignments.AsNoTracking()
            .SingleAsync(a => a.Id == assignmentId)).ArrivedAt;
        foreach (var response in responses)
        {
            var body = await response.Content.ReadFromJsonAsync<ArrivalBodyDto>();
            Assert.Equal(stored, body!.ArrivedAt);
            Assert.Equal(ExpertFlows.TestLatitude, body.Latitude);
            Assert.Equal(ExpertFlows.TestLongitude, body.Longitude);
        }

        expert.Dispose();
    }

    [Fact]
    public async Task ArrivalWithoutCoordinates_Is400_AndWritesNothing()
    {
        // §5.1: "arrival without location is not sent — the BRD requires all three values".
        var (expert, assignmentId, visa) = await Assigned();

        var response = await ExpertFlows.PressArrived(expert.Client, assignmentId, null, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("location_required", body!["error"]);
        await AssertNothingArrived(assignmentId, visa);
        expert.Dispose();
    }

    [Fact]
    public async Task ArrivalWithOnlyOneCoordinate_Is400()
    {
        var (expert, assignmentId, visa) = await Assigned();

        var response = await ExpertFlows.PressArrived(
            expert.Client, assignmentId, ExpertFlows.TestLatitude, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingArrived(assignmentId, visa);
        expert.Dispose();
    }

    [Theory]
    [InlineData(91d, 0d)]
    [InlineData(-91d, 0d)]
    [InlineData(0d, 181d)]
    [InlineData(0d, -181d)]
    public async Task ImpossibleCoordinates_Are400_AndWriteNothing(double latitude, double longitude)
    {
        var (expert, assignmentId, visa) = await Assigned();

        var response = await ExpertFlows.PressArrived(expert.Client, assignmentId, latitude, longitude);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("invalid_location", body!["error"]);
        await AssertNothingArrived(assignmentId, visa);
        expert.Dispose();
    }

    [Fact]
    public async Task TheQueuedArrival_DrainsToSent_AndReachesNext3()
    {
        // The slice's definition of done: Arrived is visible in the outbox as `sent`.
        var (expert, assignmentId, visa) = await Assigned();
        // The dequeue claims a batch across the whole table and cannot be scoped to one test's rows,
        // so another test's leftovers could fill the batch and leave this row unclaimed.
        await fixture.ClearQueue();

        var pressedAt = fixture.Time.GetUtcNow();
        await ExpertFlows.PressArrived(expert.Client, assignmentId);
        await fixture.OutboxProcessor.RunOnce(default);

        await using var db = fixture.CreateDbContext();
        var message = await db.Set<Next3OutboxMessage>().AsNoTracking()
            .SingleAsync(m => m.VisaNo == visa && m.Operation == Next3OutboxOperations.UpdateArrival);
        Assert.Equal(Next3OutboxStatuses.Sent, message.Status);
        Assert.Equal(fixture.Time.GetUtcNow().UtcDateTime, message.SentAt);
        Assert.Null(message.LastError);

        var arrival = Assert.Single(fixture.ArrivalsRecordedFor(visa));
        Assert.Equal(assignmentId.ToString(), arrival.ClientRef);
        // An instant, not a date and a time. Splitting it here would throw the offset away while the
        // row is still durable, and an expert arriving at 01:30 GST would be pushed to NEXT3 as
        // arriving the previous day — see ArrivalInfo. The split is RealNext3Client's (#6).
        Assert.Equal(pressedAt, arrival.Info.OccurredAt);
        Assert.Equal(TimeSpan.Zero, arrival.Info.OccurredAt.Offset);
        Assert.Equal(ExpertFlows.TestLatitude, arrival.Info.Latitude);
        Assert.Equal(ExpertFlows.TestLongitude, arrival.Info.Longitude);
        expert.Dispose();
    }

    [Fact]
    public async Task ArrivedIsNotAPreconditionForCapture()
    {
        // §5.1's recorded interpretation, asserted rather than assumed: the diagram implies an order,
        // the BRD never states the gate, and a roadside expert whose GPS is slow must not be blocked
        // from photographing. Slice 2.5's capture UI inherits this.
        var (expert, assignmentId, _) = await Assigned();

        var upload = await MediaFlows.UploadCarPhoto(expert.Client, assignmentId);

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        await using var db = fixture.CreateDbContext();
        var assignment = await db.ExpertAssignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId);
        Assert.Null(assignment.ArrivedAt);
        expert.Dispose();
    }

    [Fact]
    public async Task ArrivedAt_IsVisibleOnE1AndE2()
    {
        var (expert, assignmentId, visa) = await Assigned();
        await ExpertFlows.PressArrived(expert.Client, assignmentId);

        var list = await expert.Client.GetFromJsonAsync<List<AssignmentListItemDto>>(
            "/api/expert/assignments");
        var detail = await expert.Client.GetFromJsonAsync<AssignmentDetailDto>(
            $"/api/expert/assignments/{assignmentId}");

        var row = Assert.Single(list!, i => i.VisaNo == visa);
        Assert.Equal(fixture.Time.GetUtcNow().UtcDateTime, row.ArrivedAt);
        Assert.Equal(row.ArrivedAt, detail!.ArrivedAt);
        expert.Dispose();
    }

    [Fact]
    public async Task AnotherExpertsAssignment_Is404_NotForbidden_AndWritesNothing()
    {
        // 403 would confirm the id exists; 404 tells a prober nothing (the E2 rule, unchanged).
        var (owner, assignmentId, visa) = await Assigned();
        using var other = await fixture.CreateMappedExpert();

        var response = await ExpertFlows.PressArrived(other.Client, assignmentId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingArrived(assignmentId, visa);
        owner.Dispose();
    }

    [Fact]
    public async Task UnknownAssignmentId_Is404()
    {
        using var expert = await fixture.CreateMappedExpert();

        var response = await ExpertFlows.PressArrived(expert.Client, Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Garage)]
    [InlineData(UserRole.ClaimOfficer)]
    [InlineData(UserRole.Broker)]
    [InlineData(UserRole.Admin)]
    public async Task NonExpertRoles_AreForbidden(UserRole role)
    {
        var user = await fixture.CreateUser(role, UserStatus.Active);
        using var client = fixture.CreateClient();
        client.WithBearer((await fixture.Login(client, user.Phone)).AccessToken);

        var response = await ExpertFlows.PressArrived(client, Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        using var client = fixture.CreateClient();

        var response = await ExpertFlows.PressArrived(client, Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>One stamp, one push, one audit row — whatever the caller did.</summary>
    private async Task AssertExactlyOneArrival(Guid assignmentId, string visaNo)
    {
        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.Set<Next3OutboxMessage>().CountAsync(m =>
            m.VisaNo == visaNo && m.Operation == Next3OutboxOperations.UpdateArrival));
        Assert.Equal(1, await db.Set<AuditLog>().CountAsync(a =>
            a.Action == AuditActions.AssignmentArrived && a.EntityId == assignmentId));
    }

    private async Task AssertNothingArrived(Guid assignmentId, string visaNo)
    {
        await using var db = fixture.CreateDbContext();
        var assignment = await db.ExpertAssignments.AsNoTracking().SingleAsync(a => a.Id == assignmentId);
        Assert.Null(assignment.ArrivedAt);
        Assert.Null(assignment.ArrivalLat);
        Assert.Null(assignment.ArrivalLng);
        Assert.Equal(0, await db.Set<Next3OutboxMessage>().CountAsync(m =>
            m.VisaNo == visaNo && m.Operation == Next3OutboxOperations.UpdateArrival));
        Assert.Equal(0, await db.Set<AuditLog>().CountAsync(a =>
            a.Action == AuditActions.AssignmentArrived && a.EntityId == assignmentId));
    }

    /// <summary>An expert with one assignment whose claim is seeded in the fake.</summary>
    private async Task<(MappedExpert Expert, Guid AssignmentId, string VisaNo)> Assigned()
    {
        var expert = await fixture.CreateMappedExpert();
        var visa = fixture.SeedClaim();
        var assignmentRef = ExpertFlows.NextRef();
        await fixture.Inject(visa, expert.Next3Id, assignmentRef);

        await using var db = fixture.CreateDbContext();
        var assignment = await db.ExpertAssignments.AsNoTracking()
            .SingleAsync(a => a.Next3AssignmentRef == assignmentRef);
        return (expert, assignment.Id, visa);
    }
}
