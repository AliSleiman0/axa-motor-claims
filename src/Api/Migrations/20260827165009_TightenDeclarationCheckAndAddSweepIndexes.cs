﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Api.Migrations
{
    /// <summary>
    /// Slice 7.2: one clause on `CK_declaration_decision`, and the three indexes this slice's new
    /// sweeps need. **Named for both**, rather than for the constraint alone as the slice card had
    /// it — a migration whose name hides half of what it does is a bad thing to read in six months.
    ///
    /// **The constraint: a `visa_no`, if present, is not empty.** The third biconditional (slice 4.1)
    /// pins "an approved-or-later state carries a visa" — but an empty string is not null, so an
    /// `approved` row with `visa_no = ''` satisfied every clause. That is worse than the case it
    /// rules out: a missing visa strands the declaration's deferred documents, which is at least
    /// inert, while an empty one lets `approve` enqueue them — so the pushes go out addressed to a
    /// claim that does not exist, and the failure lands 26 h 36 m later as a `failed` row on A2, or
    /// does not land at all. `LEN`, not `&lt;&gt; ''`: T-SQL ignores trailing spaces in an equality
    /// comparison, so `[visa_no] &lt;&gt; ''` is *true* for three spaces. `LEN` ignores them too and
    /// therefore returns 0, refusing both cases with one predicate. Raised by the db-review.
    ///
    /// **The three indexes**, each for one query this slice adds that runs hourly and normally
    /// returns nothing — the shape that scans a growing table for ever if nobody notices.
    /// `IX_document_deferred` serves the stranded-deferred re-queue; `IX_declaration_state_decided_at`
    /// the rejected-declaration retention sweep, which `(state, submitted_at)` cannot serve because a
    /// rejected row's `submitted_at` says when the garage filed it; `IX_device_token_token_hash` the
    /// handset-displacement lookup, which asks "who else holds this token?" with no user and so could
    /// not seek the `(user_id, token_hash)` unique index — a table scan on every launch of the shell,
    /// which re-registers by design.
    ///
    /// **`IX_document_deferred` is declared with EF's named-index overload for a reason worth
    /// keeping.** Written as `HasIndex(...).HasDatabaseName(...)` over the same two properties as
    /// `IX_document_owner_kind_owner_id`, EF treats the two as one definition and the generated
    /// migration *dropped* the unfiltered one — silently taking E1's media counts and every
    /// per-owner document list off an index. Caught by reading the generated file, which is what step
    /// 2 of the `add-ef-migration` skill is for.
    ///
    /// **`document.push_status` also became a concurrency token in this slice and produced no DDL**,
    /// which is why nothing about it appears below: a token is a predicate EF adds to the UPDATE, not
    /// a column. It shows in the snapshot only.
    ///
    /// **`Down()` is data-safe and genuinely reversible.** It restores the previous constraint SQL
    /// verbatim — a strictly weaker predicate, so SQL Server's validation against existing data
    /// cannot fail — and drops three indexes, which loses no information.
    ///
    /// **`Up()` is not free, and it can fail on exactly the data it was written for.** SQL Server
    /// validates a new CHECK against every existing row, so a legacy `approved` row carrying an empty
    /// visa stops the deployment — §10 runs the bundle *before* the new revision goes live, which is
    /// the right way round but means the release halts. There is no repair step here on purpose: the
    /// obvious ones are both wrong (nulling the visa violates the third clause for an approved row,
    /// and `WITH NOCHECK` leaves the row in place unvalidated), so it is a data decision rather than
    /// a migration one. The pre-check to run first is
    /// `SELECT id FROM declaration WHERE visa_no IS NOT NULL AND LEN(visa_no) = 0`, and it is expected
    /// to return nothing: `OfficerEndpoints` has trimmed and refused `visa_required` since slice 4.1,
    /// so this constraint is defence against a future writer rather than a fix for a current one.
    /// The three `CREATE INDEX`es are each one pass over their table under a schema lock; at #20's
    /// volumes, milliseconds.
    ///
    /// **The `--no-transaction` hazard, same as every constraint migration in this repo.** Under
    /// `dotnet ef database update` or §10's migration bundle the drop/add pair runs in one
    /// transaction, so a failed `ADD` rolls the `DROP` back. Executed from a script generated with
    /// `--no-transaction`, the `DROP` commits and a failed `ADD` leaves `declaration` with **no
    /// decision constraint at all** — every combination of state, officer, timestamp and visa
    /// writable until somebody notices. Recorded per migration because it is per migration; the 3.1,
    /// 4.1, 5.1, 5.2 and 5.3 files say the same thing.
    /// </summary>
    public partial class TightenDeclarationCheckAndAddSweepIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_declaration_decision",
                table: "declaration");

            migrationBuilder.CreateIndex(
                name: "IX_document_deferred",
                table: "document",
                columns: new[] { "owner_kind", "owner_id" },
                filter: "[push_status] = 'deferred'");

            migrationBuilder.CreateIndex(
                name: "IX_device_token_token_hash",
                table: "device_token",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "IX_declaration_state_decided_at",
                table: "declaration",
                columns: new[] { "state", "decided_at" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_declaration_decision",
                table: "declaration",
                sql: "(CASE WHEN [state] IN ('approved', 'rejected', 'repair_docs_submitted', 'repairs_in_progress') THEN 1 ELSE 0 END) = (CASE WHEN [officer_user_id] IS NOT NULL THEN 1 ELSE 0 END) AND (CASE WHEN [state] IN ('approved', 'rejected', 'repair_docs_submitted', 'repairs_in_progress') THEN 1 ELSE 0 END) = (CASE WHEN [decided_at] IS NOT NULL THEN 1 ELSE 0 END) AND (CASE WHEN [state] IN ('approved', 'repair_docs_submitted', 'repairs_in_progress') THEN 1 ELSE 0 END) = (CASE WHEN [visa_no] IS NOT NULL THEN 1 ELSE 0 END) AND ([visa_no] IS NULL OR LEN([visa_no]) > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_document_deferred",
                table: "document");

            migrationBuilder.DropIndex(
                name: "IX_device_token_token_hash",
                table: "device_token");

            migrationBuilder.DropIndex(
                name: "IX_declaration_state_decided_at",
                table: "declaration");

            migrationBuilder.DropCheckConstraint(
                name: "CK_declaration_decision",
                table: "declaration");

            migrationBuilder.AddCheckConstraint(
                name: "CK_declaration_decision",
                table: "declaration",
                sql: "(CASE WHEN [state] IN ('approved', 'rejected', 'repair_docs_submitted', 'repairs_in_progress') THEN 1 ELSE 0 END) = (CASE WHEN [officer_user_id] IS NOT NULL THEN 1 ELSE 0 END) AND (CASE WHEN [state] IN ('approved', 'rejected', 'repair_docs_submitted', 'repairs_in_progress') THEN 1 ELSE 0 END) = (CASE WHEN [decided_at] IS NOT NULL THEN 1 ELSE 0 END) AND (CASE WHEN [state] IN ('approved', 'repair_docs_submitted', 'repairs_in_progress') THEN 1 ELSE 0 END) = (CASE WHEN [visa_no] IS NOT NULL THEN 1 ELSE 0 END)");
        }
    }
}
