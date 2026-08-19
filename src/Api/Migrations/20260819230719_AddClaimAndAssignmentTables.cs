using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimAndAssignmentTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "claim",
                columns: table => new
                {
                    visa_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    policy_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    plate_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    insured_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    insured_phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    car_make_model = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    city = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    accident_date = table.Column<DateOnly>(type: "date", nullable: false),
                    fetched_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claim", x => x.visa_no);
                });

            migrationBuilder.CreateTable(
                name: "expert_assignment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    visa_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    expert_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    next3_assignment_ref = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    received_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    notified_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    opened_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    arrived_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    arrival_lat = table.Column<double>(type: "float", nullable: true),
                    arrival_lng = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expert_assignment", x => x.id);
                    table.ForeignKey(
                        name: "FK_expert_assignment_app_user_expert_user_id",
                        column: x => x.expert_user_id,
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expert_assignment_expert_user_id_received_at",
                table: "expert_assignment",
                columns: new[] { "expert_user_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_expert_assignment_next3_assignment_ref",
                table: "expert_assignment",
                column: "next3_assignment_ref",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "claim");

            migrationBuilder.DropTable(
                name: "expert_assignment");
        }
    }
}
