using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // design.md paragraph 4's `document` table - every media item in the system.
            //
            // Two columns are not in paragraph 4's list; both are recorded in scope-decisions.md:
            //   outbox_message_id - paragraph 7.3 requires the cleanup query to join on outbox 'sent'
            //                       "structurally, not by convention", and the only other join key is
            //                       the clientRef inside the outbox row's JSON payload, which is a
            //                       convention and unindexed. No FK: a relationship would put
            //                       Next3OutboxMessage into the media module's model configuration,
            //                       which architecture rule 4 forbids.
            //   blob_deleted_at   - the retention sweep must be idempotent, and "have this photo's
            //                       bytes been cleaned up yet?" is a support question.
            //
            // owner_id has no FK either: paragraph 4 makes it polymorphic (owner_kind names the table).
            //
            // IX_document_outbox_message_id is UNIQUE, and that is a safety property rather than
            // tidiness: one confirmed push must license the deletion of exactly one document's bytes.
            // Two rows sharing the id would both be swept when that single push landed, destroying
            // bytes for a file NEXT3 never received - the exact outcome paragraph 7.3 exists to make
            // impossible. Its filter is IS NOT NULL only; adding blob_deleted_at IS NULL would stop
            // enforcing uniqueness the moment a row was swept.
            //
            // CK_document_push_status_outbox is the matching cross-column invariant: 'queued' with no
            // outbox row is a document that is never pushed, never listed on A2 and never eligible
            // for deletion - retained for ever and invisible everywhere.
            migrationBuilder.CreateTable(
                name: "document",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    owner_kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    owner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    bucket = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    doc_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    origin = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    clarity_result = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    blob_key = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    push_status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    outbox_message_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    blob_deleted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document", x => x.id);
                    table.CheckConstraint("CK_document_bucket", "[bucket] IN ('expert_report', 'insured_car_photo', 'insured_documents', 'tp_car_photo', 'tp_documents')");
                    table.CheckConstraint("CK_document_clarity_result", "[clarity_result] IN ('passed', 'not_applicable')");
                    table.CheckConstraint("CK_document_origin", "[origin] IN ('captured', 'uploaded')");
                    table.CheckConstraint("CK_document_owner_kind", "[owner_kind] IN ('assignment', 'declaration', 'broker_request')");
                    table.CheckConstraint("CK_document_push_status", "[push_status] IN ('queued', 'n/a')");
                    table.CheckConstraint("CK_document_push_status_outbox", "([push_status] = 'queued' AND [outbox_message_id] IS NOT NULL) OR ([push_status] = 'n/a' AND [outbox_message_id] IS NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_blob_key",
                table: "document",
                column: "blob_key",
                filter: "[blob_deleted_at] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_document_outbox_message_id",
                table: "document",
                column: "outbox_message_id",
                unique: true,
                filter: "[outbox_message_id] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "created_at", "blob_key" });

            migrationBuilder.CreateIndex(
                name: "IX_document_owner_kind_owner_id",
                table: "document",
                columns: new[] { "owner_kind", "owner_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Structurally this reverses Up(). Operationally it is DESTRUCTIVE beyond this table, in
            // the same way the outbox migration's Down() is, and for a related reason.
            //
            // This table is the only record of which blobs are claimed, and the only home for
            // paragraph 9's provenance metadata - who uploaded a photo, whether it was captured or
            // picked from a gallery, what the clarity gate said. NEXT3 has the file; it has none of
            // that. Drop the table and, once Retention:OrphanBlobHours has elapsed, the orphan sweep
            // finds nothing claiming anything in the transit container and deletes EVERY blob -
            // including the bytes named by pending and failed outbox rows, which survive in
            // next3_outbox and would then be retried against files that no longer exist. That reaches
            // AXA as missing photos, which is the problem this project exists to solve.
            //
            // Before rolling this back in any live environment: drain the queue, export this table,
            // or disable the cleanup worker (Retention:CleanupEnabled=false) first.
            migrationBuilder.DropTable(
                name: "document");
        }
    }
}
