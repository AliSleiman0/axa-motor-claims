using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <summary>
    /// Slice 5.1 adds design.md §5.2's G4 buckets — `repair_photo`, `discharge` and `invoice` — as
    /// three §7.1 rows. `CK_document_bucket` is generated from `MediaBuckets.All`, so the whole
    /// migration is that constraint being rewritten from ten values to thirteen, which is the point of
    /// making a bucket a migration rather than a string.
    ///
    /// **`Down()` is only safe against a table with no rows in the three new buckets.** It restores
    /// the ten-value constraint, and SQL Server validates a new CHECK against existing data, so a
    /// rollback after any garage has sent a discharge or an invoice fails outright — noisy rather than
    /// silent, which is the right way round, but know it before reaching for it. Unlike 4.1's
    /// declaration buckets these are `PushTiming.Immediate`, so by the time such a row exists its
    /// outbox row is queued or already `sent` and its bytes may be at AXA and swept from the transit
    /// container: re-bucketing rather than deleting is the recovery, and the outbox row and the blob
    /// are separate objects with their own lifecycles (§7.3).
    ///
    /// **That noisiness depends on the rollback running in a transaction**, which `dotnet ef database
    /// update` and §10's migration bundle both give it: the failed `ADD` rolls the `DROP` back and the
    /// thirteen-value constraint survives. Executed from a script generated with `--no-transaction`,
    /// the `DROP` commits and the `ADD` fails, leaving `document.bucket` with **no check constraint at
    /// all** — every invented bucket string writable until somebody notices. Recorded per migration
    /// because it is per migration; the 3.1 and 4.1 files say the same thing.
    /// </summary>
    public partial class AddRepairBuckets : Migration
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
                sql: "[bucket] IN ('approval_image', 'damage_diagram', 'discharge', 'expert_report', 'garage_car_photo', 'garage_documents', 'insured_car_photo', 'insured_documents', 'invoice', 'repair_photo', 'tp_car_photo', 'tp_documents', 'voice_note')");
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
                sql: "[bucket] IN ('approval_image', 'damage_diagram', 'expert_report', 'garage_car_photo', 'garage_documents', 'insured_car_photo', 'insured_documents', 'tp_car_photo', 'tp_documents', 'voice_note')");
        }
    }
}
