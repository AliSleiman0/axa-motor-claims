using System.Net;
using System.Net.Http.Json;
using Api.Infrastructure;
using Api.Modules.Audit;
using Api.Modules.Users;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

[Collection("api")]
public sealed class AuditLogTests(ApiFixture fixture)
{
    [Fact]
    public async Task OtpVerify_Success_WritesLoginSucceededRow()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var client = fixture.CreateClient();
        await fixture.Login(client, user.Phone);

        await using var db = fixture.CreateDbContext();
        var row = await db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == AuditActions.LoginSucceeded && a.EntityId == user.Id);
        Assert.Equal(user.Id, row.ActorUserId);
        Assert.Equal(AuditEntityKinds.AppUser, row.EntityKind);
        Assert.Equal(fixture.Time.GetUtcNow().UtcDateTime, row.At);
    }

    [Fact]
    public async Task OtpVerify_WrongCode_WritesLoginFailedRow_WithNullActor()
    {
        var user = await fixture.CreateUser(UserRole.Broker, UserStatus.Active);
        var client = fixture.CreateClient();
        fixture.Time.Advance(TimeSpan.FromSeconds(61));
        (await client.PostAsJsonAsync("/auth/otp/request", new { phone = user.Phone })).EnsureSuccessStatusCode();
        var realCode = fixture.Sms.LastOtpFor(user.Phone);
        var wrongCode = realCode == "000000" ? "000001" : "000000";

        var verify = await client.PostAsJsonAsync("/auth/otp/verify", new { phone = user.Phone, code = wrongCode });
        Assert.Equal(HttpStatusCode.Unauthorized, verify.StatusCode);

        await using var db = fixture.CreateDbContext();
        var row = await db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == AuditActions.LoginFailed && a.Detail!.Contains(user.Phone));
        Assert.Null(row.ActorUserId);
        Assert.Null(row.EntityId);
        Assert.Contains("code_rejected", row.Detail);
    }

    [Fact]
    public async Task OtpVerify_NoActiveUser_WritesLoginFailedRow_WithNullActor()
    {
        // The only reachable no_active_user path: a valid code whose user was deactivated
        // between OTP request and verify (the request endpoint gates on active status).
        var user = await fixture.CreateUser(UserRole.Garage, UserStatus.Active);
        var client = fixture.CreateClient();
        fixture.Time.Advance(TimeSpan.FromSeconds(61));
        (await client.PostAsJsonAsync("/auth/otp/request", new { phone = user.Phone })).EnsureSuccessStatusCode();
        var code = fixture.Sms.LastOtpFor(user.Phone);

        await using (var db = fixture.CreateDbContext())
        {
            var reloaded = await db.Users.SingleAsync(u => u.Id == user.Id);
            reloaded.Status = UserStatus.Inactive;
            await db.SaveChangesAsync();
        }

        var verify = await client.PostAsJsonAsync("/auth/otp/verify", new { phone = user.Phone, code });
        Assert.Equal(HttpStatusCode.Unauthorized, verify.StatusCode);

        await using (var db = fixture.CreateDbContext())
        {
            var row = await db.Set<AuditLog>().AsNoTracking()
                .SingleAsync(a => a.Action == AuditActions.LoginFailed && a.Detail!.Contains(user.Phone));
            Assert.Null(row.ActorUserId);
            Assert.Null(row.EntityId);
            Assert.Contains("no_active_user", row.Detail);
        }
    }

    [Fact]
    public async Task InviteVerify_Activation_WritesUserActivatedRow()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Invited);
        var token = await fixture.IssueInvite(user.Id);
        var client = fixture.CreateClient();
        (await client.PostAsJsonAsync("/auth/invite/accept", new { token })).EnsureSuccessStatusCode();
        var code = fixture.Sms.LastOtpFor(user.Phone);
        (await client.PostAsJsonAsync("/auth/invite/verify", new { token, code })).EnsureSuccessStatusCode();

        await using var db = fixture.CreateDbContext();
        var row = await db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == AuditActions.UserActivated && a.EntityId == user.Id);
        Assert.Equal(user.Id, row.ActorUserId);
        Assert.Equal(AuditEntityKinds.AppUser, row.EntityKind);
    }

    [Fact]
    public async Task AuditLog_RawSqlUpdate_IsRejectedByTrigger()
    {
        var rowId = await ArrangeOneAuditRow();

        await using var db = fixture.CreateDbContext();
        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlAsync($"UPDATE audit_log SET [action] = 'tampered' WHERE id = {rowId}"));
        Assert.Equal(50051, ex.Number);

        var row = await db.Set<AuditLog>().AsNoTracking().SingleAsync(a => a.Id == rowId);
        Assert.NotEqual("tampered", row.Action); // row unchanged
    }

    [Fact]
    public async Task AuditLog_RawSqlDelete_IsRejectedByTrigger()
    {
        var rowId = await ArrangeOneAuditRow();

        await using var db = fixture.CreateDbContext();
        var ex = await Assert.ThrowsAsync<SqlException>(() =>
            db.Database.ExecuteSqlAsync($"DELETE FROM audit_log WHERE id = {rowId}"));
        Assert.Equal(50051, ex.Number);

        Assert.Equal(1, await db.Set<AuditLog>().CountAsync(a => a.Id == rowId)); // row survives
    }

    [Fact]
    public void AuditWriter_HasNoUpdateOrDeleteSurface()
    {
        // The code side of append-only: the writer exposes exactly one operation.
        var methods = typeof(AuditWriter).GetMethods(
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.DeclaredOnly);
        var method = Assert.Single(methods);
        Assert.Equal(nameof(AuditWriter.Append), method.Name);
    }

    // ---- slice 7.2: §9's "approval/rejection with comments **hash**" ----

    /// <summary>
    /// The detail carried a bare <c>HasComment</c> boolean until this slice, which answers no question
    /// a claims dispute asks. A hash does: it shows that the comment on screen today is the comment
    /// the officer wrote, without the trail becoming a second copy of the text.
    /// </summary>
    /// <remarks>
    /// The expected value is computed from the <c>declaration_comment.body</c> **read back from the
    /// database**, not from the string this test sent. Hashing its own input would assert the code's
    /// arithmetic back to itself and could never go red (2.4's lesson) — and the thing actually worth
    /// pinning is that the two agree about trimming, which is where they would first drift.
    /// </remarks>
    [Fact]
    public async Task Approve_WithAComment_HashesExactlyTheStoredText()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var visa = fixture.SeedClaim();
        var declarationId = await garage.SubmittedDeclaration();

        (await officer.UploadApprovalImage(declarationId)).EnsureSuccessStatusCode();
        (await officer.Approve(declarationId, visa, "  PLACEHOLDER approved with conditions  "))
            .EnsureSuccessStatusCode();

        await using var db = fixture.CreateDbContext();
        var stored = await db.DeclarationComments.AsNoTracking()
            .Where(c => c.DeclarationId == declarationId)
            .Select(c => c.Body)
            .SingleAsync();

        var audit = await fixture.AuditRow(AuditActions.DeclarationApproved, declarationId);

        Assert.Contains(TokenHashing.Hash(stored), audit.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reject_WithoutAComment_RecordsANullHashAndNoCommentRow()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var declarationId = await garage.SubmittedDeclaration();

        (await officer.Reject(declarationId, "   ")).EnsureSuccessStatusCode();

        Assert.Equal(0, await fixture.CommentCount(declarationId));

        var audit = await fixture.AuditRow(AuditActions.DeclarationRejected, declarationId);

        // Null rather than a hash of the empty string, for the same reason `AddComment` writes no
        // row: there is nothing to attest to, and a hash of "" would look like evidence of a comment.
        Assert.Contains("\"CommentSha256\":null", audit.Detail, StringComparison.Ordinal);
    }

    /// <summary>A login-success audit row to tamper with — created through the real flow.</summary>
    private async Task<Guid> ArrangeOneAuditRow()
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var client = fixture.CreateClient();
        await fixture.Login(client, user.Phone);

        await using var db = fixture.CreateDbContext();
        var row = await db.Set<AuditLog>().AsNoTracking()
            .SingleAsync(a => a.Action == AuditActions.LoginSucceeded && a.EntityId == user.Id);
        return row.Id;
    }
}
