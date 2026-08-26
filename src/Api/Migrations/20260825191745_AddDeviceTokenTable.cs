using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <summary>
    /// Slice 6.3 adds the native half of design.md §8's device registry: FCM registration tokens
    /// from the Android Capacitor shell, which is the only way an Android handset can be notified
    /// at all — the WebView exposes no `PushManager`, so unlike iOS there is no web-push fallback
    /// (research-capacitor.md §3, observed on the Samsung).
    ///
    /// **A second table rather than a widened `push_subscription`, and it is forced rather than
    /// chosen.** That table's `endpoint`, `p256dh` and `auth` are all `NOT NULL`, and an FCM token
    /// has none of the three; fitting one in would mean making three required columns nullable on
    /// the path §8's primary trigger runs down, so the web-push sender would start reading columns
    /// that are only sometimes there.
    ///
    /// **`Down()` is destructive in exactly the way `AddPushSubscriptionTable`'s is, and does not
    /// repair itself.** Dropping this table deletes every Android registration in the system and
    /// nothing re-creates them: a handset cannot re-register on its own, because the registration
    /// endpoint is authenticated and the shell has no token until somebody signs in and presses
    /// Enable notifications. Rolling this back anywhere real means asking every expert with the app
    /// to open it and re-enable notifications — export the table first.
    ///
    /// No `--no-transaction` hazard: the check constraint is created inline with the table rather
    /// than rewritten over existing rows, which is what made the bucket migrations (3.1, 4.1, 5.1,
    /// 5.2, 5.3, 6.1) need the warning.
    ///
    /// Deployment order is the ordinary one — this migration adds a table nothing older reads, so
    /// image-first and migration-first are both safe, unlike `AddOutboxLastAttemptAt`.
    /// </summary>
    public partial class AddDeviceTokenTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    platform = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_token", x => x.id);
                    table.CheckConstraint("CK_device_token_platform", "[platform] IN ('android')");
                    table.ForeignKey(
                        name: "FK_device_token_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_token_user_id",
                table: "device_token",
                column: "user_id",
                filter: "[revoked_at] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_device_token_user_id_token_hash",
                table: "device_token",
                columns: new[] { "user_id", "token_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_token");
        }
    }
}
