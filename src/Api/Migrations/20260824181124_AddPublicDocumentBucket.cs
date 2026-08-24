using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <summary>
    /// Slice 5.3 adds design.md §5.3's Option 2 row — `public_document`, the supporting documents a
    /// member of the public attaches from a broker's link. `CK_document_bucket` is generated from
    /// `MediaBuckets.All`, so the whole migration is that constraint being rewritten from fourteen
    /// values to fifteen, which is the point of making a bucket a migration rather than a string.
    ///
    /// **Nothing else changes**, and that is worth stating: the bucket reuses the `broker_request`
    /// owner kind slice 5.2 introduced, so `CK_document_owner_kind` is untouched; it is
    /// `PushTiming.Never`, so it writes no outbox row and `CK_document_push_status_outbox` has nothing
    /// new to allow; and it needs no column, because a public customer is an absent
    /// <c>created_by</c> — nullable since 2.3 for exactly this caller.
    ///
    /// **`Down()` is only safe against a table with no `public_document` rows.** It restores the
    /// fourteen-value constraint, and SQL Server validates a new CHECK against existing data, so a
    /// rollback after any customer has attached a file fails outright — noisy rather than silent,
    /// which is the right way round. The recovery is re-bucketing rather than deleting: these bytes
    /// never went to NEXT3 (there is no outbox row and no push to undo), but they *are* the
    /// attachments B4's email carries, and `BrokerMediaCleanupTask` keeps them until
    /// `Retention:BrokerBlobDays` after `emailed_at`.
    ///
    /// **That noisiness depends on the rollback running in a transaction**, which `dotnet ef database
    /// update` and §10's migration bundle both give it: the failed `ADD` rolls the `DROP` back and the
    /// fifteen-value constraint survives. Executed from a script generated with `--no-transaction`,
    /// the `DROP` commits and the `ADD` fails, leaving `document.bucket` with **no check constraint at
    /// all** — every invented bucket string writable until somebody notices. Recorded per migration
    /// because it is per migration; the 3.1, 4.1, 5.1 and 5.2 files say the same thing.
    /// </summary>
    public partial class AddPublicDocumentBucket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_document_bucket",
                table: "document");

            migrationBuilder.AddCheckConstraint(
                name: "CK_document_bucket",
                table: "document",
                sql: "[bucket] IN ('approval_image', 'broker_document', 'damage_diagram', 'discharge', 'expert_report', 'garage_car_photo', 'garage_documents', 'insured_car_photo', 'insured_documents', 'invoice', 'public_document', 'repair_photo', 'tp_car_photo', 'tp_documents', 'voice_note')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_document_bucket",
                table: "document");

            migrationBuilder.AddCheckConstraint(
                name: "CK_document_bucket",
                table: "document",
                sql: "[bucket] IN ('approval_image', 'broker_document', 'damage_diagram', 'discharge', 'expert_report', 'garage_car_photo', 'garage_documents', 'insured_car_photo', 'insured_documents', 'invoice', 'repair_photo', 'tp_car_photo', 'tp_documents', 'voice_note')");
        }
    }
}
