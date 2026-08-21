using Api.Infrastructure;
using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Integrations.Push.Persistence;

public sealed class PushSubscriptionConfiguration : IEntityTypeConfiguration<PushSubscription>
{
    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("push_subscription");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(s => s.UserId).HasColumnName("user_id");

        builder.Property(s => s.Endpoint).HasColumnName("endpoint")
            .HasMaxLength(PushSubscriptionLimits.EndpointLength).IsRequired();
        builder.Property(s => s.EndpointHash).HasColumnName("endpoint_hash")
            .HasMaxLength(TokenHashing.HashLength).IsRequired();
        builder.Property(s => s.P256dh).HasColumnName("p256dh")
            .HasMaxLength(PushSubscriptionLimits.P256dhLength).IsRequired();
        builder.Property(s => s.Auth).HasColumnName("auth")
            .HasMaxLength(PushSubscriptionLimits.AuthLength).IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnName("created_at");
        builder.Property(s => s.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(s => s.RevokedAt).HasColumnName("revoked_at");

        // Restrict, matching expert_assignment and broker_request: a future hard delete of an
        // app_user must not silently take the record of which devices we notified.
        builder.HasOne<AppUser>().WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // **This index is the upsert.** Re-subscribing the same browser must refresh the row rather
        // than add a second one, and a read-then-write cannot promise that: two POSTs racing (a
        // double-click, or two tabs) both pass the read and both insert. 1.5's lesson, fifth slice
        // running — the guard for "this may only happen once" belongs in the schema. The endpoint
        // handler catches the violation and updates instead.
        //
        // Scoped to the user, not global, because the card's rule is "upsert by endpoint **for the
        // caller**": two people sharing a browser profile share an endpoint, and a global unique
        // index would let whoever subscribed first quietly own it. See scope-decisions for the
        // consequence that carries.
        builder.HasIndex(s => new { s.UserId, s.EndpointHash }).IsUnique();

        // The send path: "every live subscription for this user". Filtered, because a revoked row is
        // never a send target and there is no other query that wants one.
        builder.HasIndex(s => s.UserId).HasFilter("[revoked_at] IS NULL");
    }
}
