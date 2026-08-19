using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOption2Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "broker_request",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    broker_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    option = table.Column<int>(type: "int", nullable: false),
                    state = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    insured_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    insurance_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    insured_address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    car_value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    estimated_premium = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    customer_mobile = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    submitted_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    emailed_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    email_recipient = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broker_request", x => x.id);
                    table.CheckConstraint("CK_broker_request_option", "[option] IN (1, 2)");
                    table.CheckConstraint("CK_broker_request_state", "[state] IN ('draft', 'submitted', 'link_issued', 'customer_in_progress', 'ready_to_send', 'sent', 'expired')");
                    table.ForeignKey(
                        name: "FK_broker_request_app_user_broker_user_id",
                        column: x => x.broker_user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "public_link_token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    broker_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    locked_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    row_version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_public_link_token", x => x.id);
                    table.ForeignKey(
                        name: "FK_public_link_token_broker_request_broker_request_id",
                        column: x => x.broker_request_id,
                        principalTable: "broker_request",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_broker_request_broker_user_id_state",
                table: "broker_request",
                columns: new[] { "broker_user_id", "state" });

            migrationBuilder.CreateIndex(
                name: "IX_public_link_token_broker_request_id",
                table: "public_link_token",
                column: "broker_request_id");

            migrationBuilder.CreateIndex(
                name: "IX_public_link_token_token_hash",
                table: "public_link_token",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "public_link_token");

            migrationBuilder.DropTable(
                name: "broker_request");
        }
    }
}
