using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// <c>CK_declaration_decision</c>'s fourth clause, added in slice 7.2 on the db-review's insistence:
/// a <c>visa_no</c>, if present, is not empty.
/// </summary>
/// <remarks>
/// <para>
/// The third biconditional (slice 4.1) pins "an approved-or-later state carries a visa" — but an
/// empty string is not null, so an <c>approved</c> row with <c>visa_no = ''</c> satisfied every
/// clause. That is worse than the case it rules out: a missing visa strands the declaration's
/// deferred documents, which is at least inert, while an empty one lets <c>approve</c> enqueue them,
/// so the pushes go out addressed to a claim that does not exist.
/// </para>
/// <para>
/// **Tested by raw SQL, because no code path can produce the row.** <c>OfficerEndpoints</c> has
/// trimmed and refused <c>visa_required</c> since 4.1, which is exactly the argument that would
/// retire the constraint and exactly why it exists: the service is not the only thing that will ever
/// write this table, and a seed script, a support fix or a future endpoint has no such guard.
/// </para>
/// </remarks>
[Collection("api")]
public sealed class DeclarationConstraintTests(ApiFixture fixture)
{
    [Theory]
    [InlineData("")]
    // Three spaces: `LEN` ignores trailing whitespace and therefore returns 0, which is why one
    // predicate refuses both cases. Written as `[visa_no] <> ''` the constraint would have admitted
    // this one, because T-SQL ignores trailing spaces in an equality comparison too.
    [InlineData("   ")]
    public async Task AnApprovedDeclarationWithABlankVisaIsRefusedByTheDatabase(string visaNo)
    {
        using var garage = await fixture.CreateGarage();
        var declarationId = await garage.SubmittedDeclaration();

        await using var db = fixture.CreateDbContext();
        var officerUserId = Guid.CreateVersion7();
        var decidedAt = fixture.Time.GetUtcNow().UtcDateTime;

        var ex = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync(
            $@"UPDATE declaration
               SET state = 'approved',
                   visa_no = {visaNo},
                   officer_user_id = {officerUserId},
                   decided_at = {decidedAt}
               WHERE id = {declarationId}"));

        Assert.Contains("CK_declaration_decision", ex.Message, StringComparison.Ordinal);

        // And the row is untouched — a check constraint refuses the statement, it does not half-apply
        // it, which is the whole reason the invariant lives here rather than in a service.
        var declaration = await fixture.DeclarationRow(declarationId);
        Assert.Null(declaration.VisaNo);
    }

    /// <summary>
    /// The other side, so the clause cannot be satisfied vacuously: a real visa still writes. Without
    /// this, a constraint that refused *every* update would look identical from the test above.
    /// </summary>
    [Fact]
    public async Task AnApprovedDeclarationWithARealVisaIsAccepted()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var visa = fixture.SeedClaim();
        var declarationId = await garage.SubmittedDeclaration();

        (await officer.UploadApprovalImage(declarationId)).EnsureSuccessStatusCode();
        (await officer.Approve(declarationId, visa)).EnsureSuccessStatusCode();

        Assert.Equal(visa, (await fixture.DeclarationRow(declarationId)).VisaNo);
    }
}
