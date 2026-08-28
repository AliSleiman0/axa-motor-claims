using Api.Modules.Audit;
using Api.Modules.Media;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// design.md §7.3's rejected-declaration retention gap, closed in slice 7.2 — the fourth sweep.
///
/// The gap was structural rather than an oversight. §5.2 makes rejection terminal with no resubmit
/// edge, so a rejected declaration's documents stay <c>deferred</c> and never acquire an outbox row:
/// the confirmed-push sweep needs one that reads <c>sent</c>, and the orphan sweep spares any blob a
/// live row still claims. Nothing moved them on, so an accident's photographs were kept for ever —
/// which neither §7.3's "flat forever" sizing nor §9's "not a long-term PII store" survives.
/// </summary>
[Collection("api")]
public sealed class DeclarationRetentionTests(ApiFixture fixture)
{
    [Fact]
    public async Task ARejectedDeclarationsBlobsAreSweptOnceTheWindowHasPassed()
    {
        var (declarationId, document) = await RejectedWithOneDocument();

        Assert.True(await fixture.BlobExists(document.BlobKey));

        // Inside the window first. A test that only asserted the delete would pass for a task that
        // deleted on sight, which is the one behaviour a retention rule must never have.
        await fixture.Sweep();
        Assert.True(await fixture.BlobExists(document.BlobKey));

        await BackdateDecision(declarationId, days: 31);
        await fixture.Sweep();

        Assert.False(await fixture.BlobExists(document.BlobKey));

        // The metadata row outlives its bytes by design — §9's "who uploaded which photo, when" is
        // ours to keep — and the audit row names the rule that removed them.
        var swept = Assert.Single(await fixture.DocumentRows(declarationId));
        Assert.NotNull(swept.BlobDeletedAt);

        var audit = await fixture.AuditRow(AuditActions.DocumentBlobDeleted, document.Id);
        Assert.Contains("rejected_declaration", audit.Detail, StringComparison.Ordinal);
    }

    /// <summary>
    /// **The safety property, and it is not the same test as <c>push_status</c>.** A document that
    /// has an outbox row belongs to the pipeline: its bytes are what A2's Retry re-sends, and §7.3's
    /// first sweep already owns them on the <c>sent</c> clock. Deleting them here would turn a
    /// recoverable failed push into a photograph AXA can never be given.
    /// </summary>
    [Fact]
    public async Task ADocumentWithAnOutboxRowIsNeverSweptByThisRule()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var declarationId = await garage.SubmittedDeclaration();

        var visa = fixture.SeedClaim();
        (await officer.UploadApprovalImage(declarationId)).EnsureSuccessStatusCode();
        (await officer.Approve(declarationId, visa)).EnsureSuccessStatusCode();

        // Approved, so every document is queued and carries an outbox message id. Then forced into
        // the shape the sweep looks for — rejected, decided long ago — so the *only* thing standing
        // between these bytes and deletion is the `outbox_message_id IS NULL` arm.
        await ForceRejected(declarationId);
        await BackdateDecision(declarationId, days: 365);

        var documents = await fixture.DocumentRows(declarationId);
        Assert.All(documents, d => Assert.NotNull(d.OutboxMessageId));

        await fixture.WithRetention(r => r.RejectedDeclarationBlobDays = 0, () => fixture.Sweep());

        foreach (var document in documents)
        {
            Assert.True(await fixture.BlobExists(document.BlobKey));
        }
    }

    [Fact]
    public async Task AnUndecidedDeclarationsBlobsAreNeverSwept()
    {
        using var garage = await fixture.CreateGarage();
        var declarationId = await garage.SubmittedDeclaration();
        var document = Assert.Single(await fixture.DocumentRows(declarationId));

        // Well past any window. A submitted declaration has never been decided, so there is no clock
        // to run out — and an officer is still going to look at these photographs.
        await fixture.WithRetention(r => r.RejectedDeclarationBlobDays = 0, () => fixture.Sweep());

        Assert.True(await fixture.BlobExists(document.BlobKey));
        Assert.Null(Assert.Single(await fixture.DocumentRows(declarationId)).BlobDeletedAt);
    }

    private async Task<(Guid DeclarationId, Document Document)> RejectedWithOneDocument()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var declarationId = await garage.SubmittedDeclaration();

        (await officer.Reject(declarationId, "PLACEHOLDER-not-accepted")).EnsureSuccessStatusCode();

        var document = Assert.Single(await fixture.DocumentRows(declarationId));
        Assert.Equal(DocumentPushStatuses.Deferred, document.PushStatus);
        Assert.Null(document.OutboxMessageId);

        return (declarationId, document);
    }

    /// <summary>
    /// Moves a decision into the past with raw SQL rather than the shared clock — advancing that far
    /// would sign every other test in this serialized collection out (2.1's lesson, and the idiom
    /// <c>BrokerFlows.BackdateEmailedAt</c> already uses).
    /// </summary>
    private async Task BackdateDecision(Guid declarationId, int days)
    {
        await using var db = fixture.CreateDbContext();
        var decidedAt = fixture.Time.GetUtcNow().UtcDateTime.AddDays(-days);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE declaration SET decided_at = {decidedAt} WHERE id = {declarationId}");
    }

    /// <summary>
    /// Forces a row §5.2's transitions cannot produce: rejected, but carrying documents that were
    /// already queued. The visa goes with the state because <c>CK_declaration_decision</c> would
    /// otherwise refuse it — `rejected` is decided but not linked, so it must hold no visa.
    /// </summary>
    /// <remarks>
    /// The shape is unreachable, and that is the point. The sweep's guard has to be structural rather
    /// than an inference from the state machine, because a rule that is only true while some *other*
    /// component behaves is a rule nothing is checking. This is the same argument
    /// <c>DeclarationConfiguration</c> makes for keeping the check constraint at all.
    /// </remarks>
    private async Task ForceRejected(Guid declarationId)
    {
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlAsync(
            $"UPDATE declaration SET state = 'rejected', visa_no = NULL WHERE id = {declarationId}");
    }
}
