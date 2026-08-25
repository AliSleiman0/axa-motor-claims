using System.Net;
using System.Net.Http.Json;
using Api.Modules.Audit;
using Api.Modules.Users;
using Api.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// §5.4's A2 failed-push queue (slice 6.2): what the screen lists, what Retry may touch, and what it
/// must never touch.
///
/// Two fixture hazards shape every test here.
///
/// The clock is frozen and shared, and <see cref="AuthFlows.Login"/> advances it 61 seconds to clear
/// the OTP resend throttle — so "now" before signing in is not "now" inside the handler. Arrange-time
/// values are captured into locals, the admin client is signed in **once** per test, and any
/// assertion about a timestamp the server wrote reads the clock *after* the call.
///
/// And the queue is one table shared by the whole serialized collection, so these tests clear
/// `failed` as well as `pending` before arranging: A2's list and count are table-wide, and the retry
/// suites leave `failed` rows behind on purpose.
/// </summary>
[Collection("api")]
public sealed class OutboxAdminTests(ApiFixture fixture) : IDisposable
{
    private HttpClient? _admin;

    private DateTime Now => fixture.Time.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task TheListShowsFailedRowsAndLongPendingOnes_ButNotWhatIsAboutToBeTried()
    {
        await fixture.ClearQueueAndFailures();
        var now = Now;
        var failed = await Queue("PLACEHOLDER-VISA-A2-01", Next3OutboxStatuses.Failed, now);
        // Past the one-poll horizon: sitting in the backoff schedule, which by attempt four is hours
        // long. Nothing is wrong with it, but nobody should have to wonder where the photograph went.
        var stillTrying = await Queue("PLACEHOLDER-VISA-A2-02", Next3OutboxStatuses.Pending, now.AddHours(2));
        // Inside the horizon: the worker is about to pick it up on its own.
        var dueSoon = await Queue("PLACEHOLDER-VISA-A2-03", Next3OutboxStatuses.Pending, now.AddSeconds(5));
        var processing = await Queue(
            "PLACEHOLDER-VISA-A2-04", Next3OutboxStatuses.Processing, now.AddHours(2));
        var sent = await Queue("PLACEHOLDER-VISA-A2-05", Next3OutboxStatuses.Sent, now);

        var listed = (await List()).Select(r => r.Id).ToList();

        Assert.Contains(failed, listed);
        Assert.Contains(stillTrying, listed);
        Assert.DoesNotContain(dueSoon, listed);
        // Being pushed right now. If its worker dies the lease returns it by itself (§6.3), so
        // showing it would invite an admin to act on a row nothing is wrong with.
        Assert.DoesNotContain(processing, listed);
        Assert.DoesNotContain(sent, listed);
    }

    [Fact]
    public async Task FailedRowsSortAboveStillTryingOnes_AndOldestFirstWithinEach()
    {
        await fixture.ClearQueueAndFailures();
        var now = Now;
        var younger = await Queue("PLACEHOLDER-VISA-A2-11", Next3OutboxStatuses.Failed, now);
        var stillTrying = await Queue("PLACEHOLDER-VISA-A2-12", Next3OutboxStatuses.Pending, now.AddHours(2));
        var older = await Queue("PLACEHOLDER-VISA-A2-13", Next3OutboxStatuses.Failed, now);

        // Created youngest-first and then given ages in reverse, so "failed first, oldest first
        // within each" cannot be insertion order wearing a disguise: every one of the three keys
        // disagrees with the order the rows were written in.
        await SetCreatedAt(older, now.AddHours(-3));
        await SetCreatedAt(younger, now.AddHours(-1));
        await SetCreatedAt(stillTrying, now.AddHours(-5));

        var order = (await List()).Select(r => r.Id)
            .Where(id => id == younger || id == stillTrying || id == older).ToList();

        Assert.Equal([older, younger, stillTrying], order);
    }

    [Fact]
    public async Task ARowThatHasNeverBeenClaimedReportsNoLastAttempt()
    {
        await fixture.ClearQueueAndFailures();
        var id = await Queue("PLACEHOLDER-VISA-A2-21", Next3OutboxStatuses.Failed, Now);

        var row = Assert.Single(await List(), r => r.Id == id);

        // The screen renders an em dash for this. Backfilling it from `created_at` would be inventing
        // an attempt that never happened.
        Assert.Null(row.LastAttemptAt);
        Assert.Equal(Next3OutboxOperations.UploadDocument, row.Operation);
        Assert.Equal("PLACEHOLDER-VISA-A2-21", row.VisaNo);
    }

    [Fact]
    public async Task Retry_ResetsAFailedRowToPendingAndDue_AndLeavesItsAttemptsAndErrorAlone()
    {
        await fixture.ClearQueueAndFailures();
        var id = await Queue(
            "PLACEHOLDER-VISA-A2-31", Next3OutboxStatuses.Failed, Now.AddDays(1),
            attempts: 8, lastError: "PLACEHOLDER: NEXT3 refused the document type.");

        var response = await Post($"/api/admin/outbox/{id}/retry");
        var stamped = Now;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await fixture.Row(id);
        Assert.Equal(Next3OutboxStatuses.Pending, row.Status);
        Assert.Equal(stamped, row.NextRetryAt);

        // `attempts` is the honest cumulative count A2's own annotation describes, and it is the
        // concurrency token a live worker's outcome write carries. A retry is one more attempt, not a
        // fresh 26-hour schedule.
        Assert.Equal(8, row.Attempts);

        // Kept until the next attempt records an outcome: blanking it would leave a row on the screen
        // with nothing to say about itself.
        Assert.Equal("PLACEHOLDER: NEXT3 refused the document type.", row.LastError);
    }

    [Fact]
    public async Task RetryNow_PullsALongPendingRowForward()
    {
        await fixture.ClearQueueAndFailures();
        var id = await Queue("PLACEHOLDER-VISA-A2-32", Next3OutboxStatuses.Pending, Now.AddHours(2));

        var response = await Post($"/api/admin/outbox/{id}/retry");
        var stamped = Now;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await fixture.Row(id);
        Assert.Equal(Next3OutboxStatuses.Pending, row.Status);
        Assert.Equal(stamped, row.NextRetryAt);
    }

    [Fact]
    public async Task Retry_RefusesARowThatIsBeingPushed_AndLeavesEveryColumnUntouched()
    {
        await fixture.ClearQueueAndFailures();
        var lease = Now.AddSeconds(300);
        var id = await Queue(
            "PLACEHOLDER-VISA-A2-33", Next3OutboxStatuses.Processing, lease, attempts: 3);

        var response = await Post($"/api/admin/outbox/{id}/retry");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("not_retryable", await response.Content.ReadAsStringAsync());

        // The guard is the status inside the WHERE clause, not an `if` above it. Take `Processing`'s
        // exclusion out of `RetryableRow` and this goes red: the row is claimed by a worker that is
        // about to write its outcome under `AND attempts = 3`, and pulling it back to `pending` here
        // would hand the same push to a second worker.
        var row = await fixture.Row(id);
        Assert.Equal(Next3OutboxStatuses.Processing, row.Status);
        Assert.Equal(lease, row.NextRetryAt);
        Assert.Equal(3, row.Attempts);
    }

    [Fact]
    public async Task Retry_RefusesARowThatHasAlreadyReachedNext3()
    {
        await fixture.ClearQueueAndFailures();
        var id = await Queue("PLACEHOLDER-VISA-A2-34", Next3OutboxStatuses.Sent, Now);

        Assert.Equal(HttpStatusCode.Conflict, (await Post($"/api/admin/outbox/{id}/retry")).StatusCode);
        Assert.Equal(Next3OutboxStatuses.Sent, (await fixture.Row(id)).Status);
    }

    [Fact]
    public async Task Retry_RefusesAnIdThatIsNotInTheQueue()
    {
        // The same 409 and the same word as a `sent` or `processing` row: all three mean "there is
        // nothing here for you to retry", and the screen has one sentence for it.
        var response = await Post($"/api/admin/outbox/{Guid.CreateVersion7()}/retry");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("not_retryable", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RetryAll_ResetsEveryListedRow_AndNothingElse()
    {
        await fixture.ClearQueueAndFailures();
        var now = Now;
        var failed = await Queue("PLACEHOLDER-VISA-A2-41", Next3OutboxStatuses.Failed, now.AddDays(1));
        var stillTrying = await Queue("PLACEHOLDER-VISA-A2-42", Next3OutboxStatuses.Pending, now.AddHours(2));
        var dueSoon = now.AddSeconds(5);
        var dueSoonId = await Queue("PLACEHOLDER-VISA-A2-43", Next3OutboxStatuses.Pending, dueSoon);
        var processing = await Queue(
            "PLACEHOLDER-VISA-A2-44", Next3OutboxStatuses.Processing, now.AddHours(2));
        var sent = await Queue("PLACEHOLDER-VISA-A2-45", Next3OutboxStatuses.Sent, now);

        var response = await Post("/api/admin/outbox/retry-all");
        var stamped = Now;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Exactly the list predicate — the button does what the screen shows and nothing else.
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<RetriedDto>())!.Retried);

        Assert.Equal(Next3OutboxStatuses.Pending, (await fixture.Row(failed)).Status);
        Assert.Equal(stamped, (await fixture.Row(failed)).NextRetryAt);
        Assert.Equal(stamped, (await fixture.Row(stillTrying)).NextRetryAt);

        Assert.Equal(dueSoon, (await fixture.Row(dueSoonId)).NextRetryAt);
        Assert.Equal(Next3OutboxStatuses.Processing, (await fixture.Row(processing)).Status);
        Assert.Equal(Next3OutboxStatuses.Sent, (await fixture.Row(sent)).Status);
    }

    [Fact]
    public async Task RetryAll_OnAnEmptyQueue_IsAFineAnswerRatherThanAnError()
    {
        await fixture.ClearQueueAndFailures();

        var response = await Post("/api/admin/outbox/retry-all");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await response.Content.ReadFromJsonAsync<RetriedDto>())!.Retried);
    }

    [Fact]
    public async Task TheCountCountsFailedRowsOnly()
    {
        await fixture.ClearQueueAndFailures();
        var now = Now;
        Assert.Equal(0, await Count());

        await Queue("PLACEHOLDER-VISA-A2-51", Next3OutboxStatuses.Failed, now);
        Assert.Equal(1, await Count());

        // A long-pending row is on the list but not in the badge: it is on its way, and a number that
        // rose and fell with the backoff schedule would be an alarm nobody trusts.
        await Queue("PLACEHOLDER-VISA-A2-52", Next3OutboxStatuses.Pending, now.AddHours(2));
        await Queue("PLACEHOLDER-VISA-A2-53", Next3OutboxStatuses.Processing, now.AddHours(2));
        await Queue("PLACEHOLDER-VISA-A2-54", Next3OutboxStatuses.Sent, now);

        Assert.Equal(1, await Count());
    }

    [Fact]
    public async Task ARetryIsAudited_WithTheActorAndTheMessage()
    {
        await fixture.ClearQueueAndFailures();
        var id = await Queue("PLACEHOLDER-VISA-A2-61", Next3OutboxStatuses.Failed, Now);

        Assert.Equal(HttpStatusCode.OK, (await Post($"/api/admin/outbox/{id}/retry")).StatusCode);

        // §9 names "outbox retries from A2" explicitly: an admin reaching into the queue and
        // re-sending a push is an intervention in the pipeline this project exists to make reliable.
        await using var db = fixture.CreateDbContext();
        var entry = await db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == AuditActions.OutboxPushRetried && a.EntityId == id);

        Assert.Equal(AuditEntityKinds.Next3Outbox, entry.EntityKind);
        Assert.NotNull(entry.ActorUserId);
    }

    [Fact]
    public async Task RetryAll_IsAuditedAsOneDecisionCarryingItsCount()
    {
        await fixture.ClearQueueAndFailures();
        await Queue("PLACEHOLDER-VISA-A2-71", Next3OutboxStatuses.Failed, Now);
        await Queue("PLACEHOLDER-VISA-A2-72", Next3OutboxStatuses.Failed, Now);
        var before = await CountAudit(AuditActions.OutboxPushesRetried);

        Assert.Equal(HttpStatusCode.OK, (await Post("/api/admin/outbox/retry-all")).StatusCode);

        await using var db = fixture.CreateDbContext();
        var entries = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == AuditActions.OutboxPushesRetried)
            .OrderBy(a => a.At).ToListAsync();

        // One row, not one per message: "somebody retried everything at 09:14" is a single decision,
        // and two rows would misrepresent it as two.
        Assert.Equal(before + 1, entries.Count);
        Assert.Null(entries[^1].EntityId);
        Assert.Contains("2", entries[^1].Detail);
    }

    [Theory]
    [InlineData(UserRole.Expert)]
    [InlineData(UserRole.Garage)]
    [InlineData(UserRole.ClaimOfficer)]
    [InlineData(UserRole.Broker)]
    public async Task EveryOtherRoleIsRefusedTheQueue(UserRole role)
    {
        // The shared RolePolicyTests matrix probes `/api/admin/ping`, which this group does not
        // serve, so the policy on *these* routes is covered only here.
        var user = await fixture.CreateUser(role, UserStatus.Active);
        using var client = fixture.CreateClient();
        client.WithBearer((await fixture.Login(client, user.Phone)).AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/outbox")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/outbox/count")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.PostAsync("/api/admin/outbox/retry-all", null)).StatusCode);
    }

    [Fact]
    public async Task AnonymousIsRefusedTheQueue()
    {
        using var client = fixture.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/outbox")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/admin/outbox/retry-all", null)).StatusCode);
    }

    public void Dispose() => _admin?.Dispose();

    private sealed record RetriedDto(int Retried);

    private sealed record FailedCountDto(int Failed);

    /// <summary>
    /// One signed-in admin per test. Signing in advances the shared clock past the OTP resend
    /// throttle, so doing it once keeps the arrange and the assertions a known distance apart instead
    /// of a minute further apart on every call.
    /// </summary>
    private async Task<HttpClient> Admin() => _admin ??= await fixture.CreateAdminClient();

    private async Task<Guid> Queue(
        string visaNo, string status, DateTime nextRetryAt, int? attempts = null, string? lastError = null)
    {
        var id = await fixture.EnqueueDocument(visaNo, OutboxFlows.NextClientRef());
        await fixture.Reshape(id, status, nextRetryAt, attempts, lastError);
        return id;
    }

    private async Task SetCreatedAt(Guid messageId, DateTime createdAt)
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlAsync(
            $"UPDATE next3_outbox SET created_at = {createdAt} WHERE id = {messageId}");
    }

    private async Task<List<OutboxAdminRowDto>> List() =>
        (await (await Admin()).GetFromJsonAsync<List<OutboxAdminRowDto>>("/api/admin/outbox"))!;

    private async Task<int> Count() =>
        (await (await Admin()).GetFromJsonAsync<FailedCountDto>("/api/admin/outbox/count"))!.Failed;

    private async Task<HttpResponseMessage> Post(string path) =>
        await (await Admin()).PostAsync(path, null);

    private async Task<int> CountAudit(string action)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Set<AuditLog>().AsNoTracking().CountAsync(a => a.Action == action);
    }
}
