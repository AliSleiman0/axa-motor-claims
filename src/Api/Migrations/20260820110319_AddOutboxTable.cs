using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "next3_outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    visa_no = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    operation = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    last_error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    next_retry_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    sent_at = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_next3_outbox", x => x.id);
                    table.CheckConstraint("CK_next3_outbox_operation", "[operation] IN ('upload_document', 'update_arrival', 'push_approval')");
                    table.CheckConstraint("CK_next3_outbox_status", "[status] IN ('pending', 'processing', 'sent', 'failed')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_next3_outbox_status_next_retry_at",
                table: "next3_outbox",
                columns: new[] { "status", "next_retry_at" });

            migrationBuilder.CreateIndex(
                name: "IX_next3_outbox_visa_no",
                table: "next3_outbox",
                column: "visa_no");

            // Hand-added: design.md paragraph 6.3's dequeue, the first of the three stored procedures
            // paragraph 3 sanctions. READPAST is SQL Server's SKIP LOCKED, so a second worker steps
            // over rows a first worker has already claimed instead of blocking behind them.
            //
            // The claim is ONE atomic statement. Reading status and then writing it would be two
            // steps that two workers both pass - the trap that bit public_link_token in slice 1.5.
            // The guard belongs in the schema.
            //
            // Two deliberate departures from the SQL quoted in paragraph 6.3, both recorded in
            // scope-decisions.md:
            //   @now         - the worker's clock, not the database's, so the retry schedule is
            //                  testable against a frozen FakeTimeProvider.
            //   @lease_seconds - the claim pushes next_retry_at out, so a worker that dies mid-push
            //                  has its row reclaimed rather than stranded in 'processing' forever,
            //                  invisible to the A2 failed list. Safe because every push carries a
            //                  stable clientRef.
            //
            // next3_outbox must stay trigger-free: OUTPUT inserted.* is why.
            // Schema-qualified on both sides (here and in OutboxDequeue): an unqualified CREATE lands
            // in the migrating principal's default schema, and paragraph 10 runs migrations as a
            // pipeline step that need not be the same principal the app connects as.
            // CREATE OR ALTER so re-applying against an environment where the procedure survived a
            // partial rollback repairs it instead of aborting with Msg 2714.
            migrationBuilder.Sql("""
                CREATE OR ALTER PROCEDURE dbo.next3_outbox_dequeue
                    @batch_size int,
                    @now datetime2 = NULL,
                    @lease_seconds int = 300,
                    @max_attempts int = 8
                AS
                BEGIN
                    SET NOCOUNT ON;

                    DECLARE @t datetime2 = COALESCE(@now, SYSUTCDATETIME());

                    -- A worker that dies before recording an outcome has its row reclaimed by the
                    -- lease. If it keeps dying on the same row, the MaxAttempts cut-off in
                    -- application code never runs, so the row would be reclaimed for ever: pushed
                    -- again every lease period and never visible on A2. Retire it here instead, so
                    -- the lease cannot relocate the very invisibility it exists to remove.
                    -- Same hints as the claim below, and for the same reason: a row another worker
                    -- is holding right now is by definition not abandoned, so step over it rather
                    -- than block on it. Without READPAST here this statement blocks on any live
                    -- claim and the whole procedure stops being non-blocking.
                    UPDATE dbo.next3_outbox WITH (UPDLOCK, READPAST, ROWLOCK)
                    SET status     = 'failed',
                        last_error = COALESCE(
                            last_error, 'Abandoned: lease expired with no outcome recorded.')
                    WHERE status = 'processing'
                      AND next_retry_at <= @t
                      AND attempts >= @max_attempts;

                    -- Deliberately unordered, like the SQL in paragraph 6.3. A CTE with
                    -- ORDER BY created_at was tried, to drain a backlog oldest-first; it makes the
                    -- sort materialise the candidate set, which takes locks on rows READPAST is
                    -- supposed to let a second worker step over, and
                    -- AClaimedBatchDoesNotBlockASecondWorker goes from passing to timing out.
                    -- Non-blocking concurrent drain matters more than ordering among rows that are
                    -- all already due, so ordering loses. Revisit only with an execution plan in
                    -- hand - see the note in scope-decisions.md.
                    UPDATE TOP (@batch_size) dbo.next3_outbox WITH (UPDLOCK, READPAST, ROWLOCK)
                    SET status        = 'processing',
                        attempts      = attempts + 1,
                        next_retry_at = DATEADD(second, @lease_seconds, @t)
                    OUTPUT inserted.*
                    WHERE next_retry_at <= @t
                      AND status IN ('pending', 'processing');
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Unlike the audit_log trigger, a procedure is not owned by the table - dropping
            // next3_outbox would leave it behind, so it needs its own drop.
            //
            // Structurally this reverses Up() completely. Operationally it is only safe against a
            // drained queue: every pending/processing/failed row is a NEXT3 write that has not
            // happened, and the blob key it names lives nowhere else, so rolling back with a backlog
            // orphans those blobs - "AXA is missing photos", which is the problem this project
            // exists to solve. Drain or export before rolling this back in any live environment.
            migrationBuilder.Sql("DROP PROCEDURE IF EXISTS dbo.next3_outbox_dequeue;");

            migrationBuilder.DropTable(
                name: "next3_outbox");
        }
    }
}
