using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Declarations.Persistence;

public sealed class DeclarationConfiguration : IEntityTypeConfiguration<Declaration>
{
    public void Configure(EntityTypeBuilder<Declaration> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var states = Quoted(DeclarationStates.All);
        var decided = Quoted(DeclarationStates.Decided);
        var linked = Quoted(DeclarationStates.Linked);

        builder.ToTable("declaration", table =>
        {
            table.HasCheckConstraint("CK_declaration_state", $"[state] IN ({states})");

            // §5.2's decision columns, pinned to the states that imply them — the same discipline as
            // CK_document_push_status_outbox, and load-bearing for the same reason.
            //
            // An `approved` row with no visa is a declaration whose every deferred document can never
            // be pushed: retained for ever, invisible on A2 (there is no outbox row to list), and
            // surfacing weeks later as "AXA is missing the garage's photos" — the precise failure this
            // project exists to remove. A `draft` row *with* a visa is the mirror image: a declaration
            // linked to a claim nobody approved.
            //
            // Both are unrepresentable now, in the schema rather than in DeclarationService's head,
            // because the service is not the only thing that will ever write this table.
            //
            // Every side is wrapped in its own CASE, including the `IN` tests. T-SQL has no boolean
            // *value*: `([state] IN ('a','b')) = 1` is a syntax error, not a comparison, so each
            // biconditional has to be spelled as two 0/1 expressions on either side of `=`.
            //
            // **Three biconditionals, one per column, rather than one over a conjunction.** Written as
            // `... = (CASE WHEN officer IS NOT NULL AND decided_at IS NOT NULL ...)`, the undecided
            // side is satisfied whenever *either* column is null — so a draft carrying an officer id
            // and no timestamp would pass. Unreachable through the entity's private setters today,
            // which is exactly the argument that would retire this constraint; the constraint exists
            // because the entity will not always be the only writer.
            //
            // **The fourth clause, added in slice 7.2 on the db-review's insistence: a visa, if
            // present, is not empty.** `NOT NULL` was doing all the work, and an empty string is not
            // null. An `approved` row carrying `visa_no = ''` satisfied every biconditional above and
            // is worse than the missing-visa case the third one rules out: its deferred documents are
            // enqueued rather than stranded, so the push happens, addressed to a claim that does not
            // exist — a photograph rejected at NEXT3's end, or worse, accepted under nothing.
            //
            // `LEN` rather than `DATALENGTH` or a `<> ''` comparison, and the difference matters:
            // T-SQL ignores trailing spaces in an equality compare, so `[visa_no] <> ''` is *true*
            // for a value of three spaces, while `LEN` also ignores them and therefore returns 0 —
            // refusing an all-blank visa with the same predicate rather than needing a second one.
            table.HasCheckConstraint(
                "CK_declaration_decision",
                $"(CASE WHEN [state] IN ({decided}) THEN 1 ELSE 0 END) "
                + "= (CASE WHEN [officer_user_id] IS NOT NULL THEN 1 ELSE 0 END) "
                + $"AND (CASE WHEN [state] IN ({decided}) THEN 1 ELSE 0 END) "
                + "= (CASE WHEN [decided_at] IS NOT NULL THEN 1 ELSE 0 END) "
                + $"AND (CASE WHEN [state] IN ({linked}) THEN 1 ELSE 0 END) "
                + "= (CASE WHEN [visa_no] IS NOT NULL THEN 1 ELSE 0 END) "
                + "AND ([visa_no] IS NULL OR LEN([visa_no]) > 0)");
        });

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(d => d.GarageUserId).HasColumnName("garage_user_id");

        // **The concurrency token** (design.md §5.2, slice 4.1). EF adds `state` to the WHERE of every
        // UPDATE of this row, so two simultaneous approvals cannot both succeed: the second finds no
        // row, throws DbUpdateConcurrencyException, and the endpoint answers 409.
        //
        // 2.4 rejected this idiom for `arrived_at` because a token applies to *every* write of the
        // entity, and a concurrent `opened_at` save would then have failed for no reason. `state` is
        // the opposite case — it is what every transition changes, and comments are inserts into their
        // own table rather than edits to this one, so there is no innocent write to punish.
        builder.Property(d => d.State).HasColumnName("state").HasMaxLength(24)
            .HasConversion(s => s.ToDbValue(), v => DeclarationStates.FromDbValue(v))
            .IsConcurrencyToken();

        builder.Property(d => d.PlateNo).HasColumnName("plate_no").HasMaxLength(20).IsRequired();
        builder.Property(d => d.InsuredName).HasColumnName("insured_name").HasMaxLength(200);
        builder.Property(d => d.Note).HasColumnName("note").HasMaxLength(1000);
        builder.Property(d => d.VisaNo).HasColumnName("visa_no").HasMaxLength(50);
        builder.Property(d => d.OfficerUserId).HasColumnName("officer_user_id");
        builder.Property(d => d.CreatedAt).HasColumnName("created_at");
        builder.Property(d => d.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(d => d.DecidedAt).HasColumnName("decided_at");
        builder.Property(d => d.RepairsStartedAt).HasColumnName("repairs_started_at");
        builder.Property(d => d.RepairDocsSubmittedAt).HasColumnName("repair_docs_submitted_at");

        // Restrict on both, matching expert_assignment and broker_request: a future hard delete of an
        // app_user must not silently take a garage's declaration history — and with it §9's answer to
        // "who filed this claim and who approved it" — along with it.
        builder.HasOne<AppUser>().WithMany()
            .HasForeignKey(d => d.GarageUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AppUser>().WithMany()
            .HasForeignKey(d => d.OfficerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // G1: "my declarations, newest first" (§5.2).
        builder.HasIndex(d => new { d.GarageUserId, d.CreatedAt });

        // O1's inbox: every garage's submitted declarations, oldest first — an officer works a queue,
        // so the one that has been waiting longest is the one they want.
        builder.HasIndex(d => new { d.State, d.SubmittedAt });

        // Slice 7.2's rejected-declaration retention sweep, which runs hourly and normally returns
        // nothing: without this it reads every rejected declaration ever filed to find the few past
        // the window. `(state, submitted_at)` above cannot serve it — a rejected row's `submitted_at`
        // says when the garage filed it, not when the officer decided.
        builder.HasIndex(d => new { d.State, d.DecidedAt });

        // Deliberately no FK to `claim` on visa_no, for the same reason expert_assignment has none:
        // §4 makes that cache disposable and deletable at any time, and a foreign key would turn a
        // NEXT3 outage that emptied it into lost declarations.
    }

    private static string Quoted(IEnumerable<string> values) =>
        string.Join(", ", values.Order(StringComparer.Ordinal).Select(v => $"'{v}'"));
}
