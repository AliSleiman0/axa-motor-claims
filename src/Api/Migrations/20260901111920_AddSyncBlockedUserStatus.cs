using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// `status` also became the EF concurrency token this slice (`AppUserConfiguration`) — no DDL
    /// for that: `IsConcurrencyToken()` is purely an EF-side annotation on an ordinary column, the
    /// same shape `declaration.state` already uses, and is invisible to `has-pending-model-changes`
    /// (this migration had to be regenerated to pick it up into the snapshot — check the snapshot
    /// by eye after adding one, this project's own recorded lesson).
    /// </remarks>
    public partial class AddSyncBlockedUserStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_app_user_status",
                table: "app_user");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "app_user",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AddCheckConstraint(
                name: "CK_app_user_status",
                table: "app_user",
                sql: "[status] IN ('invited', 'active', 'inactive', 'sync_blocked')");
        }

        /// <inheritdoc />
        /// <remarks>
        /// Structurally reverses Up() but is only operationally safe against a database with no
        /// `sync_blocked` rows: the AlterColumn below narrows `nvarchar(12)` back to `nvarchar(10)`,
        /// and `sync_blocked` is exactly 12 characters, so SQL Server raises a truncation error on
        /// that ALTER COLUMN itself — before the CHECK constraint is even reached — the moment any
        /// row holds the value. Rolling back after slice 7.5's sync task has actually blocked a
        /// supplier therefore fails loudly rather than silently truncating or corrupting anything.
        /// Same shape as this project's other rollback-vs-live-data notes (e.g. the outbox migration).
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_app_user_status",
                table: "app_user");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "app_user",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(12)",
                oldMaxLength: 12);

            migrationBuilder.AddCheckConstraint(
                name: "CK_app_user_status",
                table: "app_user",
                sql: "[status] IN ('invited', 'active', 'inactive')");
        }
    }
}
