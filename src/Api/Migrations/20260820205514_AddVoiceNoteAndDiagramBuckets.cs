using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <summary>
    /// Slice 3.1 adds §5.1's voice note and damage diagram as two §7.1 buckets. `CK_document_bucket`
    /// is generated from `MediaBuckets.All`, so the whole migration is that constraint being
    /// rewritten — which is the point of making a bucket a migration rather than a string.
    ///
    /// **`Down()` is only safe against a table with no rows in the two new buckets.** It restores the
    /// five-value constraint, and SQL Server validates a new CHECK against existing data, so a
    /// rollback after any expert has recorded a voice note or drawn a diagram fails outright — noisy
    /// rather than silent, which is the right way round, but know it before reaching for it. Delete
    /// or re-bucket those rows first, and remember their blobs and outbox rows are separate objects
    /// with their own lifecycles (§7.3).
    ///
    /// **That noisiness depends on the rollback running in a transaction**, which `dotnet ef database
    /// update` and §10's migration bundle both give it: the failed `ADD` rolls the `DROP` back and the
    /// seven-value constraint survives. Executed from a script generated with `--no-transaction`, the
    /// `DROP` commits and the `ADD` fails, leaving `document.bucket` with **no check constraint at
    /// all** — every invented bucket string writable until somebody notices, which is precisely the
    /// property that makes adding a bucket a reviewable migration rather than a string appearing.
    /// </summary>
    public partial class AddVoiceNoteAndDiagramBuckets : Migration
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
                sql: "[bucket] IN ('damage_diagram', 'expert_report', 'insured_car_photo', 'insured_documents', 'tp_car_photo', 'tp_documents', 'voice_note')");
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
                sql: "[bucket] IN ('expert_report', 'insured_car_photo', 'insured_documents', 'tp_car_photo', 'tp_documents')");
        }
    }
}
