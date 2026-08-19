using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    recipient_address = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    template = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    sent_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification", x => x.id);
                    table.CheckConstraint("CK_notification_channel", "[channel] IN ('push', 'sms', 'email')");
                    table.CheckConstraint("CK_notification_status", "[status] IN ('queued', 'sent', 'failed')");
                    table.ForeignKey(
                        name: "FK_notification_app_user_recipient_user_id",
                        column: x => x.recipient_user_id,
                        principalTable: "app_user",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_recipient_user_id_created_at",
                table: "notification",
                columns: new[] { "recipient_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_status",
                table: "notification",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification");
        }
    }
}
