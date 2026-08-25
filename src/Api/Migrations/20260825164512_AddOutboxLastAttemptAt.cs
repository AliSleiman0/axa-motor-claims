using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <summary>
    /// Slice 6.2 — `next3_outbox.last_attempt_at`, the column behind A2's "Last tried" (design.md
    /// §5.4). **Nothing on the row records when it was last tried.** `sent_at` is only ever written
    /// on success, `created_at` is when it was enqueued, and `next_retry_at` is overwritten with the
    /// lease deadline the instant a row is claimed — so while a push is in flight the one
    /// time-looking column says a moment in the *future*. An admin answering "where is that
    /// photograph" needs to know whether this has been tried in the last minute or the last day.
    ///
    /// **The stamp lives in the dequeue procedure, not in the worker**, for the reason the `attempts`
    /// increment does: the claim *is* the attempt, and a worker that dies before recording an outcome
    /// still tried. It is deliberately **not** written by the abandoned-row retire at the top of the
    /// procedure — retiring a row is giving up on it, and stamping there would make this column read
    /// "last given up on" for exactly the rows A2 is showing.
    ///
    /// **The column is nullable and stays that way.** Rows enqueued before this migration were never
    /// claimed under a procedure that stamps, and there is no honest value to backfill them with; the
    /// screen renders an em dash. Adding a nullable column is a metadata-only change in SQL Server,
    /// so this half is cheap even against a large queue.
    ///
    /// **`Down()` restores the `AddOutboxTable` body byte-for-byte, and the order matters.** The
    /// procedure is restored *before* the column is dropped, because the body installed by `Up()`
    /// names a column the drop removes. And the restore is byte-for-byte because the only thing worse
    /// than a rollback that fails is one that succeeds with a subtly different dequeue: this
    /// procedure holds the lease semantics, the `READPAST` hints and the retire rule, and a drifted
    /// restore would change how the whole queue behaves without changing anything anybody reads. Both
    /// bodies in this file were extracted from `20260820110319_AddOutboxTable.cs` rather than
    /// retyped.
    ///
    /// **Rolling back the database alone breaks every dequeue — roll the image back first.** Raised
    /// by the db-review. `Down()` restores a procedure whose `OUTPUT inserted.*` no longer returns
    /// `last_attempt_at`, while an app image built after this slice maps that column: EF's `FromSql`
    /// throws "the required column was not present" on every claim, so NEXT3 pushes stop dead and the
    /// queue fills with nothing on A2 to say why. §10 rolls back by re-pointing to the previous image
    /// digest and states no ordering, so the ordering is stated here, where somebody will be standing
    /// when it matters: **previous image first, then this migration.** The reverse of `Up()`'s
    /// deployment order, and for the same reason — an old image ignores a column it does not know,
    /// but no image can read one that is gone. `AddOutboxTable`'s own `Down()` carries an operational
    /// caveat of exactly this shape.
    ///
    /// `DropColumn` then discards every stamped value. Accepted, and worth naming: the column is
    /// display-only, no other column or row depends on it, and the next claim reconstructs it.
    ///
    /// **No `--no-transaction` hazard here**, unlike the check-constraint migrations (3.1, 4.1, 5.1,
    /// 5.2, 5.3, 6.1): there is no drop-then-recreate pair that can half-apply. `CREATE OR ALTER` is
    /// idempotent and the column add is a single statement, so a partial run leaves either the old
    /// procedure with no column, or the new procedure with it — never a table with no constraint.
    /// </summary>
    public partial class AddOutboxLastAttemptAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_attempt_at",
                table: "next3_outbox",
                type: "datetime2",
                nullable: true);

            // The column has to exist before the procedure that writes it. SQL Server resolves names
            // in a procedure body lazily, so the reverse order would install fine and then fail at
            // the first dequeue - the worst kind of ordering bug, because it passes the migration.
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
                    -- last_attempt_at is stamped here, with the attempts increment, for the same
                    -- reason: the claim IS the attempt. A worker that dies before recording an
                    -- outcome still tried, and A2's "Last tried" column must say so. It is
                    -- deliberately absent from the retire statement above - giving up on a row is
                    -- not attempting it, and stamping there would make the column an admin reads
                    -- mean "last given up on" instead.
                    SET status          = 'processing',
                        attempts        = attempts + 1,
                        last_attempt_at = @t,
                        next_retry_at   = DATEADD(second, @lease_seconds, @t)
                    OUTPUT inserted.*
                    WHERE next_retry_at <= @t
                      AND status IN ('pending', 'processing');
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Procedure first, column second: the Up() body names last_attempt_at, so dropping the
            // column while that body is still installed leaves a dequeue that throws on every call.
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

            migrationBuilder.DropColumn(
                name: "last_attempt_at",
                table: "next3_outbox");
        }
    }
}
