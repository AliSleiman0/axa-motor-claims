using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <summary>
    /// Slice 4.1 adds design.md §5.2's state machine — `declaration` and `declaration_comment` — and
    /// widens `document` so a garage's media can wait for approval instead of going to NEXT3 the
    /// moment it is uploaded.
    ///
    /// **`Down()` is destructive in three ways, and only the first is loud.**
    ///
    /// It drops both new tables. Every declaration, every officer decision and every comment goes with
    /// them, and nothing reconstructs them — NEXT3 holds the approved documents but not the decision
    /// that produced them, and `audit_log` holds the events but not the rows. `document` rows whose
    /// `owner_kind` is 'declaration' **survive**, pointing at ids that no longer resolve, and
    /// `CK_document_owner_kind` still permits that value, so nothing marks them as orphaned.
    ///
    /// It drops `document.file_name`, which is the only record of what an uploader called a file. That
    /// loss is silent in both directions: a `deferred` document rolled back this way and pushed later
    /// reaches NEXT3's *Survey* folder as `019ab….pdf` instead of `invoice.pdf`, and rolling forward
    /// again re-adds the column empty. Nothing throws and nothing logs.
    ///
    /// **And it fails outright against any row the old constraints reject** — SQL Server validates a
    /// restored CHECK against existing data, so a rollback after any garage upload cannot re-add the
    /// seven-bucket `CK_document_bucket` or the two-value `CK_document_push_status`. That noisiness is
    /// the right way round, but **it depends on the rollback running in a transaction**, which
    /// `dotnet ef database update` and §10's migration bundle both provide. Executed from a script
    /// generated with `--no-transaction`, the DROPs commit and the ADDs fail, leaving `document` with
    /// **no bucket or push-status constraint at all** — every invented value writable until somebody
    /// notices. Same trap as the 3.1 migration, recorded again because it is per-migration.
    ///
    /// Before rolling this back anywhere real: export both tables and `document.file_name`, and
    /// re-bucket or delete any declaration media first.
    /// </summary>
    public partial class AddDeclarationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_document_bucket",
                table: "document");

            migrationBuilder.DropCheckConstraint(
                name: "CK_document_push_status",
                table: "document");

            migrationBuilder.DropCheckConstraint(
                name: "CK_document_push_status_outbox",
                table: "document");

            migrationBuilder.AddColumn<string>(
                name: "file_name",
                table: "document",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "declaration",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    garage_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    state = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    plate_no = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    insured_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    visa_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    officer_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    decided_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    repairs_started_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    repair_docs_submitted_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_declaration", x => x.id);
                    table.CheckConstraint("CK_declaration_decision", "(CASE WHEN [state] IN ('approved', 'rejected', 'repair_docs_submitted', 'repairs_in_progress') THEN 1 ELSE 0 END) = (CASE WHEN [officer_user_id] IS NOT NULL THEN 1 ELSE 0 END) AND (CASE WHEN [state] IN ('approved', 'rejected', 'repair_docs_submitted', 'repairs_in_progress') THEN 1 ELSE 0 END) = (CASE WHEN [decided_at] IS NOT NULL THEN 1 ELSE 0 END) AND (CASE WHEN [state] IN ('approved', 'repair_docs_submitted', 'repairs_in_progress') THEN 1 ELSE 0 END) = (CASE WHEN [visa_no] IS NOT NULL THEN 1 ELSE 0 END)");
                    table.CheckConstraint("CK_declaration_state", "[state] IN ('approved', 'draft', 'rejected', 'repair_docs_submitted', 'repairs_in_progress', 'submitted')");
                    table.ForeignKey(
                        name: "FK_declaration_app_user_garage_user_id",
                        column: x => x.garage_user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_declaration_app_user_officer_user_id",
                        column: x => x.officer_user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "declaration_comment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    declaration_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_declaration_comment", x => x.id);
                    table.ForeignKey(
                        name: "FK_declaration_comment_app_user_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_declaration_comment_declaration_declaration_id",
                        column: x => x.declaration_id,
                        principalTable: "declaration",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_document_bucket",
                table: "document",
                sql: "[bucket] IN ('approval_image', 'damage_diagram', 'expert_report', 'garage_car_photo', 'garage_documents', 'insured_car_photo', 'insured_documents', 'tp_car_photo', 'tp_documents', 'voice_note')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_document_push_status",
                table: "document",
                sql: "[push_status] IN ('deferred', 'n/a', 'queued')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_document_push_status_outbox",
                table: "document",
                sql: "([push_status] = 'queued' AND [outbox_message_id] IS NOT NULL) OR ([push_status] IN ('deferred', 'n/a') AND [outbox_message_id] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_declaration_garage_user_id_created_at",
                table: "declaration",
                columns: new[] { "garage_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_declaration_officer_user_id",
                table: "declaration",
                column: "officer_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_declaration_state_submitted_at",
                table: "declaration",
                columns: new[] { "state", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_declaration_comment_author_user_id",
                table: "declaration_comment",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_declaration_comment_declaration_id_created_at",
                table: "declaration_comment",
                columns: new[] { "declaration_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "declaration_comment");

            migrationBuilder.DropTable(
                name: "declaration");

            migrationBuilder.DropCheckConstraint(
                name: "CK_document_bucket",
                table: "document");

            migrationBuilder.DropCheckConstraint(
                name: "CK_document_push_status",
                table: "document");

            migrationBuilder.DropCheckConstraint(
                name: "CK_document_push_status_outbox",
                table: "document");

            migrationBuilder.DropColumn(
                name: "file_name",
                table: "document");

            migrationBuilder.AddCheckConstraint(
                name: "CK_document_bucket",
                table: "document",
                sql: "[bucket] IN ('damage_diagram', 'expert_report', 'insured_car_photo', 'insured_documents', 'tp_car_photo', 'tp_documents', 'voice_note')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_document_push_status",
                table: "document",
                sql: "[push_status] IN ('queued', 'n/a')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_document_push_status_outbox",
                table: "document",
                sql: "([push_status] = 'queued' AND [outbox_message_id] IS NOT NULL) OR ([push_status] = 'n/a' AND [outbox_message_id] IS NULL)");
        }
    }
}
