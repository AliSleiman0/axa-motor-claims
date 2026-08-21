using Api.Integrations.Next3;
using Api.Modules.Audit;
using Api.Modules.Declarations;
using Api.Modules.Media;
using Api.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>
/// The slice's definition of done, and the two things about the schema that no other test reaches.
///
/// The chain test is the one that matters for the week-4 demo: it is beat 4 of
/// <c>docs/demo-week4.md</c> end to end, on the fakes, in one method.
/// </summary>
[Collection("api")]
public sealed class DeclarationChainTests(ApiFixture fixture)
{
    [Fact]
    public async Task TheWholeChainReachesNext3sSurveyFolder()
    {
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var visa = fixture.SeedClaim();

        // The dequeue claims a batch across the whole table and cannot be scoped to one test's rows,
        // so the queue is cleared first (2.2's rule).
        await fixture.ClearQueue();

        // 1. Draft, and two documents attached before anyone has chosen a visa.
        var id = await garage.CreateDraft(insuredName: "PLACEHOLDER Insured");
        (await garage.UploadSurvey(id, "invoice.pdf")).EnsureSuccessStatusCode();
        (await garage.UploadCarPhoto(id)).EnsureSuccessStatusCode();

        var deferred = await fixture.DocumentRows(id);
        Assert.Equal(2, deferred.Count);
        Assert.All(deferred, d =>
        {
            Assert.Equal(DocumentPushStatuses.Deferred, d.PushStatus);
            Assert.Null(d.OutboxMessageId);
        });

        // 2. Submit — the officers hear about it.
        (await garage.Submit(id)).EnsureSuccessStatusCode();
        Assert.Equal(DeclarationState.Submitted, (await fixture.DeclarationRow(id)).State);

        // 3. The officer renders and uploads #18's approval image, then approves. Two calls on
        //    purpose: a render that fails must take nothing with it.
        (await officer.UploadApprovalImage(id)).EnsureSuccessStatusCode();
        (await officer.Approve(id, visa, "PLACEHOLDER approved by AXA")).EnsureSuccessStatusCode();

        // 4. Every document is now queued, and only now.
        var queued = await fixture.DocumentRows(id);
        Assert.Equal(3, queued.Count);
        Assert.All(queued, d =>
        {
            Assert.Equal(DocumentPushStatuses.Queued, d.PushStatus);
            Assert.NotNull(d.OutboxMessageId);
        });

        // 5. Drain the queue exactly as the worker does.
        await fixture.OutboxProcessor.RunOnce(CancellationToken.None);

        await using (var db = fixture.CreateDbContext())
        {
            var rows = await db.Set<Next3OutboxMessage>().AsNoTracking()
                .Where(m => m.VisaNo == visa)
                .ToListAsync();

            Assert.Equal(3, rows.Count);
            Assert.All(rows, m => Assert.Equal(Next3OutboxStatuses.Sent, m.Status));
            Assert.All(rows, m => Assert.NotNull(m.SentAt));
        }

        // 6. And NEXT3 received them — in the *Survey* folder, with clientRef = the document id, which
        //    is what makes every retry idempotent (#32 is NEXT3's half of that bargain).
        var received = fixture.FakeNext3().RecordedDocuments.Where(d => d.VisaNo == visa).ToList();
        Assert.Equal(3, received.Count);
        Assert.All(received, d => Assert.Equal(Next3Folders.Survey, d.Doc.Folder));

        var ids = queued.Select(d => d.Id.ToString()).ToHashSet(StringComparer.Ordinal);
        Assert.All(received, d => Assert.Contains(d.ClientRef, ids));

        // The garage's own file name survived the wait, which is the entire reason `file_name` exists:
        // this push was built in a later request, from the row rather than from a request header.
        Assert.Contains(received, d => d.Doc.FileName == "invoice.pdf");

        // The doc types are the placeholder codes for these buckets, never invented values (#12).
        Assert.Contains(received, d => d.Doc.DocType == "PLACEHOLDER-DOC-12");
        Assert.Contains(received, d => d.Doc.DocType == "PLACEHOLDER-DOC-13");
        Assert.Contains(received, d => d.Doc.DocType == "PLACEHOLDER-DOC-08");

        // 7. The trail §9 requires: "every declaration transition with actor".
        //
        // Asserted as a set with its actors, not as a sequence. The suite's clock is frozen, so all
        // three rows share a timestamp and `ORDER BY at` is a tie the database is free to break either
        // way — 3.4's lesson about tests asserting an ordering the query never promised. Who did what
        // is the part a claims dispute turns on anyway, and that is assertable.
        await using (var db = fixture.CreateDbContext())
        {
            var trail = await db.Set<AuditLog>().AsNoTracking()
                .Where(a => a.EntityId == id && a.EntityKind == AuditEntityKinds.Declaration)
                .Select(a => new { a.Action, a.ActorUserId })
                .ToListAsync();

            Assert.Equal(3, trail.Count);
            Assert.Contains(
                trail,
                a => a.Action == AuditActions.DeclarationCreated && a.ActorUserId == garage.User.Id);
            Assert.Contains(
                trail,
                a => a.Action == AuditActions.DeclarationSubmitted && a.ActorUserId == garage.User.Id);
            Assert.Contains(
                trail,
                a => a.Action == AuditActions.DeclarationApproved && a.ActorUserId == officer.User.Id);
        }

        // 8. And the garage can now start repairs — the last transition this slice owns.
        (await garage.StartRepairs(id)).EnsureSuccessStatusCode();
        Assert.Equal(DeclarationState.RepairsInProgress, (await fixture.DeclarationRow(id)).State);
    }

    [Fact]
    public async Task WhenTheApprovalCommitFails_NoDocumentIsLeftQueued()
    {
        // The structural half of atomicity, in the shape OutboxAtomicityTests established: stage the
        // approval's writes and one row the database will reject, and assert the batch takes
        // everything with it. This is the assertion that cannot be reached over HTTP — there is no
        // seam to fail a request part way through — so it is made against the mechanism directly, and
        // the honest limit is that it proves the *transaction*, not the endpoint's use of it.
        //
        // What it rules out is the version of this code that called SaveChanges per document, or
        // opened its own transaction and committed the state before enqueuing: either would leave a
        // declaration approved with only some of its documents ever reaching AXA.
        using var garage = await fixture.CreateGarage();
        using var officer = await fixture.CreateOfficer();
        var visa = fixture.SeedClaim();

        var id = await garage.CreateDraft();
        (await garage.UploadSurvey(id)).EnsureSuccessStatusCode();
        (await garage.Submit(id)).EnsureSuccessStatusCode();
        (await officer.UploadApprovalImage(id)).EnsureSuccessStatusCode();

        await using (var db = fixture.CreateDbContext())
        {
            var declaration = await db.Declarations.SingleAsync(d => d.Id == id);
            var documents = await db.Documents
                .Where(d => d.OwnerKind == DocumentOwnerKinds.Declaration && d.OwnerId == id)
                .ToListAsync();

            declaration.Approve(officer.User.Id, visa, fixture.Time.GetUtcNow().UtcDateTime);

            db.DeclarationComments.Add(new DeclarationComment
            {
                Id = Guid.CreateVersion7(),
                DeclarationId = id,
                AuthorUserId = officer.User.Id,
                Body = "PLACEHOLDER approved",
                CreatedAt = fixture.Time.GetUtcNow().UtcDateTime,
            });

            var writer = new OutboxWriter(db, fixture.Time);
            foreach (var document in documents)
            {
                document.OutboxMessageId = writer.EnqueueDocument(
                    visa,
                    new DocumentPush(
                        Next3Folders.Survey, document.DocType!, document.FileName!,
                        document.ContentType, document.BlobKey),
                    document.Id.ToString());
                document.PushStatus = DocumentPushStatuses.Queued;
            }

            // The poison: an operation CK_next3_outbox_operation refuses, so SQL Server rolls the
            // whole batch back rather than EF refusing it in the change tracker.
            db.Set<Next3OutboxMessage>().Add(new Next3OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                VisaNo = visa,
                Operation = "PLACEHOLDER-not-an-operation",
                Payload = "{}",
                Status = Next3OutboxStatuses.Pending,
                NextRetryAt = fixture.Time.GetUtcNow().UtcDateTime,
                CreatedAt = fixture.Time.GetUtcNow().UtcDateTime,
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        // Nothing survived: not the state, not the visa, not the comment, not one document flip.
        var row = await fixture.DeclarationRow(id);
        Assert.Equal(DeclarationState.Submitted, row.State);
        Assert.Null(row.VisaNo);
        Assert.Equal(0, await fixture.CommentCount(id));
        Assert.All(
            await fixture.DocumentRows(id),
            d =>
            {
                Assert.Equal(DocumentPushStatuses.Deferred, d.PushStatus);
                Assert.Null(d.OutboxMessageId);
            });

        await using (var verify = fixture.CreateDbContext())
        {
            Assert.Empty(await verify.Set<Next3OutboxMessage>().AsNoTracking()
                .Where(m => m.VisaNo == visa).ToListAsync());
        }
    }

    [Theory]
    // A decided state missing its officer, and missing its timestamp — the two halves the db-reviewer
    // found a single conjunction would have let through.
    [InlineData("approved", false, true, true)]
    [InlineData("approved", true, false, true)]
    // A decided state with no visa: an approval whose documents could never be pushed.
    [InlineData("approved", true, true, false)]
    // And the mirror image — an undecided declaration already linked to a claim.
    [InlineData("draft", false, false, true)]
    [InlineData("draft", true, false, false)]
    [InlineData("submitted", false, true, false)]
    // Rejection carries a decision but never a visa (§5.2 pushes nothing on rejection).
    [InlineData("rejected", true, true, true)]
    public async Task TheDecisionConstraintRejectsAHalfWrittenDecision(
        string state, bool officer, bool decidedAt, bool visa)
    {
        // No row had ever been tested against CK_declaration_decision — the DDL runs on every
        // MigrateAsync so the T-SQL parses, but parsing is not the same as refusing anything. These
        // rows are unreachable through the entity's private setters, which is precisely the argument
        // that would delete the constraint; it exists because the entity will not always be the only
        // writer of this table.
        using var garage = await fixture.CreateGarage();
        var id = Guid.CreateVersion7();
        var now = fixture.Time.GetUtcNow().UtcDateTime;

        await using var db = fixture.CreateDbContext();

        var thrown = await Assert.ThrowsAsync<SqlWriteException>(async () =>
        {
            try
            {
                await db.Database.ExecuteSqlAsync(
                    $"""
                     INSERT INTO declaration
                         (id, garage_user_id, state, plate_no, visa_no, officer_user_id,
                          created_at, decided_at)
                     VALUES
                         ({id}, {garage.User.Id}, {state}, {DeclarationFlows.NextPlate()},
                          {(visa ? "PLACEHOLDER-VISA-0001" : null)},
                          {(officer ? garage.User.Id : (Guid?)null)},
                          {now}, {(decidedAt ? now : (DateTime?)null)})
                     """);
            }
            catch (Exception ex)
            {
                throw new SqlWriteException(ex.Message, ex);
            }
        });

        Assert.Contains("CK_declaration_decision", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>Wrapper so the assertion is about our own INSERT and not any exception in the block.</summary>
    private sealed class SqlWriteException(string message, Exception inner) : Exception(message, inner);
}
