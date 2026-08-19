using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileTablesAndAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    entity_kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    entity_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    detail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "FK_audit_log_app_user_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "app_user",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "broker_profile",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    iris_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broker_profile", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_broker_profile_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "claim_officer_profile",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    next3_user = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claim_officer_profile", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_claim_officer_profile_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expert_profile",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    next3_id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    inactivated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expert_profile", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_expert_profile_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "garage_profile",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    contact_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    mobile = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    next3_id = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    opening_hours = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    active = table.Column<bool>(type: "bit", nullable: false),
                    inactivated_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_garage_profile", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_garage_profile_app_user_user_id",
                        column: x => x.user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_actor_user_id",
                table: "audit_log",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_entity_kind_entity_id",
                table: "audit_log",
                columns: new[] { "entity_kind", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_expert_profile_next3_id",
                table: "expert_profile",
                column: "next3_id",
                unique: true,
                filter: "[next3_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_garage_profile_next3_id",
                table: "garage_profile",
                column: "next3_id",
                unique: true,
                filter: "[next3_id] IS NOT NULL");

            // Hand-added: append-only is enforced structurally, not by convention (design.md §4/§9).
            // AuditLogConfiguration declares HasTrigger so EF avoids OUTPUT-clause inserts.
            // Down() needs no matching drop — dropping the table drops its triggers.
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_audit_log_append_only ON audit_log
                INSTEAD OF UPDATE, DELETE
                AS BEGIN
                    THROW 50051, 'audit_log is append-only (design.md paragraphs 4 and 9).', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "broker_profile");

            migrationBuilder.DropTable(
                name: "claim_officer_profile");

            migrationBuilder.DropTable(
                name: "expert_profile");

            migrationBuilder.DropTable(
                name: "garage_profile");
        }
    }
}
