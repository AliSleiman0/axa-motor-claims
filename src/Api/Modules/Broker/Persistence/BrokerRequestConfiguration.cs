using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Broker.Persistence;

public sealed class BrokerRequestConfiguration : IEntityTypeConfiguration<BrokerRequest>
{
    public void Configure(EntityTypeBuilder<BrokerRequest> builder)
    {
        var states = string.Join(", ", BrokerRequestStates.All.Select(s => $"'{s}'"));

        builder.ToTable("broker_request", t =>
        {
            t.HasCheckConstraint("CK_broker_request_state", $"[state] IN ({states})");
            t.HasCheckConstraint("CK_broker_request_option", "[option] IN (1, 2)");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(r => r.BrokerUserId).HasColumnName("broker_user_id");
        builder.Property(r => r.Option).HasColumnName("option");
        // The concurrency token (slice 5.2), 4.1's idiom: `state` is what a transition changes, so a
        // lost race is answered `409 illegal_transition` instead of two submits both committing and
        // AXA's desk receiving the same request twice. A token on a non-rowversion column is
        // model-only — it emits no DDL, which is why the migration beside this change carries none.
        // See `BrokerRequest.State` for the two writes that do *not* change it and how each is
        // handled; a Resend, which changes no state, is claimed on `emailed_at` instead.
        builder.Property(r => r.State).HasColumnName("state").HasMaxLength(25)
            .HasConversion(s => s.ToDbValue(), v => BrokerRequestStates.FromDbValue(v))
            .IsConcurrencyToken();
        builder.Property(r => r.BrokerDisplayName).HasColumnName("broker_display_name").HasMaxLength(200);
        builder.Property(r => r.InsuredName).HasColumnName("insured_name").HasMaxLength(200);
        builder.Property(r => r.InsuranceType).HasColumnName("insurance_type").HasMaxLength(100);
        builder.Property(r => r.InsuredAddress).HasColumnName("insured_address").HasMaxLength(500);
        // Money: fixed-point, never float — a car value that round-trips to 24999.999998 in an
        // email to AXA is the kind of defect nobody finds until a customer disputes it.
        builder.Property(r => r.CarValue).HasColumnName("car_value").HasPrecision(18, 2);
        builder.Property(r => r.EstimatedPremium).HasColumnName("estimated_premium").HasPrecision(18, 2);
        builder.Property(r => r.EffectiveDate).HasColumnName("effective_date");
        builder.Property(r => r.CustomerMobile).HasColumnName("customer_mobile").HasMaxLength(16);
        builder.Property(r => r.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(r => r.EmailedAt).HasColumnName("emailed_at");
        builder.Property(r => r.EmailRecipient).HasColumnName("email_recipient").HasMaxLength(320);
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");

        // Restrict, not the default cascade: a required FK would otherwise let a future app_user
        // delete silently take a broker's request history with it. There is no hard-delete path
        // today (slice 1.3), which is exactly why the guard belongs in the schema rather than in
        // a convention someone has to remember.
        builder.HasOne<AppUser>().WithMany()
            .HasForeignKey(r => r.BrokerUserId)
            .OnDelete(DeleteBehavior.Restrict);
        // B1's list: "my requests, newest first" (slice 5.2) — literally
        // `WHERE broker_user_id = @x ORDER BY created_at DESC`, with no state predicate anywhere in
        // the module. So the key is `(broker_user_id, created_at)` and **not** the
        // `(broker_user_id, state, created_at)` the slice card asked for: with `state` between the
        // seek column and the sort column, rows inside a broker's partition are ordered by state
        // first and SQL Server sorts the whole partition anyway. Found by the db-reviewer.
        builder.HasIndex(r => new { r.BrokerUserId, r.CreatedAt });
    }
}
