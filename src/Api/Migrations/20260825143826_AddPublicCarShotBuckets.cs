using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <summary>
    /// Slice 6.1 adds design.md §5.3's five mandatory car sides — `public_car_front`,
    /// `public_car_rear`, `public_car_left`, `public_car_right`, `public_car_roof` — the photographs
    /// a member of the public must supply before an Option 2 submission is accepted.
    ///
    /// **Two changes, one migration** (5.2's lesson: fold while still uncommitted rather than stack).
    ///
    /// **`CK_document_bucket`, fifteen values to twenty.** Generated from `MediaBuckets.All`, so only
    /// the constants were authored — which is the point of making a bucket a migration rather than a
    /// string. Nothing else about the shape changes: the five reuse the `broker_request` owner kind
    /// slice 5.2 introduced, so `CK_document_owner_kind` is untouched; they are `PushTiming.Never`,
    /// so they write no outbox row and `CK_document_push_status_outbox` has nothing new to allow; and
    /// they need no column, because the side *is* the bucket — the multipart contract reads `bucket`
    /// and `origin` and nothing else, so no wire field was invented for it.
    ///
    /// **`IX_document_owner_kind_owner_id_bucket`, unique and filtered to those five.** One
    /// photograph per side, in the schema, because CLAUDE.md's first recurring bug class is that
    /// "this may only happen once" belongs in the schema or the `WHERE` and never in an `if`. A
    /// retake replaces (`PublicEndpoints.ReplacePreviousCarShot` stages the delete into the same
    /// transaction as the new row); without the index that replacement is a convention, and two
    /// `public_car_front` rows both reach AXA as attachments — `BrokerRequestEmail.Attachments`
    /// selects on the owner alone — with nothing on the email to say which is current. The filter is
    /// generated from `MediaBuckets.PublicCarShots` so a sixth side cannot arrive without the rule
    /// following it; every other bucket is deliberately outside it, since a request may carry many
    /// supporting documents and an assignment many photographs.
    ///
    /// **`Down()` is only safe against a table with no car-shot rows.** It drops the index — which
    /// loses no data — and restores the fifteen-value constraint, and SQL Server validates a new
    /// CHECK against existing data, so a rollback after any customer has photographed their car fails
    /// outright. Noisy rather than silent, which is the right way round. The recovery is re-bucketing
    /// rather than deleting: these bytes never went to NEXT3 (there is no outbox row and no push to
    /// undo), but they *are* attachments B4's email carries, and `BrokerMediaCleanupTask` keeps them
    /// until `Retention:BrokerBlobDays` after `emailed_at`.
    ///
    /// **That noisiness depends on the rollback running in a transaction**, which `dotnet ef database
    /// update` and §10's migration bundle both give it: the failed `ADD` rolls the `DROP` back and
    /// the twenty-value constraint survives. Executed from a script generated with
    /// `--no-transaction`, the `DROP` commits and the `ADD` fails, leaving `document.bucket` with
    /// **no check constraint at all** — every invented bucket string writable until somebody notices.
    /// Recorded per migration because it is per migration; the 3.1, 4.1, 5.1, 5.2 and 5.3 files say
    /// the same thing.
    ///
    /// **`Up()` is not free on a large table**, unlike its predecessors: the unique index is built
    /// over `document` and the check constraint is re-validated against every row, both under a
    /// schema-modification lock. Nothing at §4's working volumes, worth knowing before it grows.
    /// </summary>
    public partial class AddPublicCarShotBuckets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_document_bucket",
                table: "document");

            migrationBuilder.CreateIndex(
                name: "IX_document_owner_kind_owner_id_bucket",
                table: "document",
                columns: new[] { "owner_kind", "owner_id", "bucket" },
                unique: true,
                filter: "[bucket] IN ('public_car_front', 'public_car_left', 'public_car_rear', 'public_car_right', 'public_car_roof')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_document_bucket",
                table: "document",
                sql: "[bucket] IN ('approval_image', 'broker_document', 'damage_diagram', 'discharge', 'expert_report', 'garage_car_photo', 'garage_documents', 'insured_car_photo', 'insured_documents', 'invoice', 'public_car_front', 'public_car_left', 'public_car_rear', 'public_car_right', 'public_car_roof', 'public_document', 'repair_photo', 'tp_car_photo', 'tp_documents', 'voice_note')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_document_owner_kind_owner_id_bucket",
                table: "document");

            migrationBuilder.DropCheckConstraint(
                name: "CK_document_bucket",
                table: "document");

            migrationBuilder.AddCheckConstraint(
                name: "CK_document_bucket",
                table: "document",
                sql: "[bucket] IN ('approval_image', 'broker_document', 'damage_diagram', 'discharge', 'expert_report', 'garage_car_photo', 'garage_documents', 'insured_car_photo', 'insured_documents', 'invoice', 'public_document', 'repair_photo', 'tp_car_photo', 'tp_documents', 'voice_note')");
        }
    }
}
