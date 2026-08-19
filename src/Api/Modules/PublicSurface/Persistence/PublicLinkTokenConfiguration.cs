using Api.Infrastructure;
using Api.Modules.Broker;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.PublicSurface.Persistence;

public sealed class PublicLinkTokenConfiguration : IEntityTypeConfiguration<PublicLinkToken>
{
    public void Configure(EntityTypeBuilder<PublicLinkToken> builder)
    {
        builder.ToTable("public_link_token");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(t => t.BrokerRequestId).HasColumnName("broker_request_id");
        builder.Property(t => t.TokenHash).HasColumnName("token_hash")
            .HasMaxLength(TokenHashing.HashLength).IsRequired();
        builder.Property(t => t.ExpiresAt).HasColumnName("expires_at");
        builder.Property(t => t.LockedAt).HasColumnName("locked_at");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at");
        // The system's first concurrency token, and it belongs here rather than on broker_request:
        // the token *is* the capability, so locking it is the one place a submission serializes.
        // Putting it on the request instead would guard the fields while leaving the lock racy.
        builder.Property(t => t.Version).HasColumnName("row_version").IsRowVersion();

        // Cascade is right here and deliberate: a token is meaningless without its request, so if
        // the request ever goes, the capability pointing at it must go with it.
        builder.HasOne<BrokerRequest>().WithMany()
            .HasForeignKey(t => t.BrokerRequestId)
            .OnDelete(DeleteBehavior.Cascade);
        // Unique because every public request is a lookup by hash, and because two live tokens
        // hashing the same would mean two requests share one capability.
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.BrokerRequestId);
    }
}
