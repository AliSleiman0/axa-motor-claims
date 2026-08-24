using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <summary>
    /// Slice 5.2 folds three changes into one migration, because they land in the same slice and 2.2's
    /// lesson is that a second migration stacked on an uncommitted snapshot is harder to review than
    /// one regenerated:
    ///
    /// <list type="number">
    /// <item>`CK_document_bucket` rewritten from thirteen values to fourteen, adding `broker_document`
    /// — §5.3's Broker Option 1 bucket and the first under the `broker_request` owner kind (which the
    /// schema has allowed since 2.3; only the bucket list needed widening).</item>
    /// <item>`broker_request.broker_display_name` — the snapshot §5.3's public page reads, written at
    /// creation time so `Api.Modules.PublicSurface` never has to touch `Users` (architecture rule 2).
    /// Nullable because every row written before this migration genuinely has no name.</item>
    /// <item>The B1 list index becomes `(broker_user_id, created_at)`. **Not the
    /// `(broker_user_id, state, created_at)` the slice card asked for** — B1 is
    /// `WHERE broker_user_id = @x ORDER BY created_at DESC` with no state predicate anywhere in the
    /// module, and `state` between the seek column and the sort column leaves the partition ordered by
    /// state first, so SQL Server sorts it anyway. Found by the db-reviewer; the migration was
    /// regenerated rather than corrected by a second one.</item>
    /// </list>
    ///
    /// **`BrokerRequest.State` also became the EF concurrency token in this slice and produced no DDL
    /// here** — a token on a non-`rowversion` column is model-only. It is in the snapshot beside this
    /// file, and it is the once-only guard behind `POST /api/broker/requests/{id}/submit`; a reviewer
    /// looking for it in the SQL will not find it, which is why it is named here.
    ///
    /// **`Down()` loses data, in two ways worth stating separately.**
    ///
    /// It drops `broker_display_name` unconditionally, and that column is not recoverable from `Users`
    /// for a broker who has since been renamed or deactivated — after a down/up cycle those Option 2
    /// public pages simply stop naming anybody.
    ///
    /// And it restores the thirteen-value bucket constraint, which SQL Server validates against
    /// existing data, so a rollback after any broker has attached a document fails outright. Noisy
    /// rather than silent, which is the right way round — but unlike the repair buckets these rows
    /// carry `push_status = n/a` and no outbox row, so their bytes are still in the transit container
    /// until `Retention:BrokerBlobDays` after the send: re-bucketing rather than deleting is the
    /// recovery.
    ///
    /// **That noisiness depends on the rollback running in a transaction**, which `dotnet ef database
    /// update` and §10's migration bundle both give it: the failed `ADD` rolls the `DROP` back and the
    /// fourteen-value constraint survives. Executed from a script generated with `--no-transaction`,
    /// the `DROP` commits and the `ADD` fails, leaving `document.bucket` with **no check constraint at
    /// all** — every invented bucket string writable until somebody notices. Recorded per migration
    /// because it is per migration; the 3.1, 4.1 and 5.1 files say the same thing.
    /// </summary>
    public partial class AddBrokerRequestFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_document_bucket",
                table: "document");

            migrationBuilder.DropIndex(
                name: "IX_broker_request_broker_user_id_state",
                table: "broker_request");

            migrationBuilder.AddColumn<string>(
                name: "broker_display_name",
                table: "broker_request",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_document_bucket",
                table: "document",
                sql: "[bucket] IN ('approval_image', 'broker_document', 'damage_diagram', 'discharge', 'expert_report', 'garage_car_photo', 'garage_documents', 'insured_car_photo', 'insured_documents', 'invoice', 'repair_photo', 'tp_car_photo', 'tp_documents', 'voice_note')");

            migrationBuilder.CreateIndex(
                name: "IX_broker_request_broker_user_id_created_at",
                table: "broker_request",
                columns: new[] { "broker_user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_document_bucket",
                table: "document");

            migrationBuilder.DropIndex(
                name: "IX_broker_request_broker_user_id_created_at",
                table: "broker_request");

            migrationBuilder.DropColumn(
                name: "broker_display_name",
                table: "broker_request");

            migrationBuilder.AddCheckConstraint(
                name: "CK_document_bucket",
                table: "document",
                sql: "[bucket] IN ('approval_image', 'damage_diagram', 'discharge', 'expert_report', 'garage_car_photo', 'garage_documents', 'insured_car_photo', 'insured_documents', 'invoice', 'repair_photo', 'tp_car_photo', 'tp_documents', 'voice_note')");

            migrationBuilder.CreateIndex(
                name: "IX_broker_request_broker_user_id_state",
                table: "broker_request",
                columns: new[] { "broker_user_id", "state" });
        }
    }
}
