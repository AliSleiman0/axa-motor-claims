using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <summary>
    /// Slice 3.4 adds design.md §8's device registrations — the table that turns "a popup message
    /// will show on the expert mobile" from a console log into a real notification.
    ///
    /// **`Down()` is destructive in a way that does not repair itself.** Dropping this table deletes
    /// every device registration in the system, and nothing re-creates them: the browser still holds
    /// a live `pushManager` subscription and has no idea anything is wrong, while the server can no
    /// longer reach it. There is no `pushsubscriptionchange` recovery either — a service worker
    /// cannot re-register on its own, because the subscribe endpoint is authenticated and the worker
    /// has no token. So the only way back is every expert pressing "Enable notifications" again, on
    /// every device, and until they do the assignment popup silently stops arriving — which is the
    /// BRD's primary trigger and the thing this application exists to deliver.
    ///
    /// Before rolling this back anywhere real: export the table, and expect to ask people to
    /// re-enable notifications.
    /// </summary>
    public partial class AddPushSubscriptionTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "push_subscription",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    endpoint = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    endpoint_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    p256dh = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    auth = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_subscription", x => x.id);
                    table.ForeignKey(
                        name: "FK_push_subscription_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_push_subscription_user_id",
                table: "push_subscription",
                column: "user_id",
                filter: "[revoked_at] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_push_subscription_user_id_endpoint_hash",
                table: "push_subscription",
                columns: new[] { "user_id", "endpoint_hash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "push_subscription");
        }
    }
}
