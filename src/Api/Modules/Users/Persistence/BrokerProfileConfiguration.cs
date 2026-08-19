using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Users.Persistence;

public sealed class BrokerProfileConfiguration : IEntityTypeConfiguration<BrokerProfile>
{
    public void Configure(EntityTypeBuilder<BrokerProfile> builder)
    {
        builder.ToTable("broker_profile");

        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).HasColumnName("user_id").ValueGeneratedNever();
        builder.Property(p => p.IrisCode).HasColumnName("iris_code").HasMaxLength(50).IsRequired();
        builder.Property(p => p.Email).HasColumnName("email").HasMaxLength(256).IsRequired();

        builder.HasOne<AppUser>().WithOne().HasForeignKey<BrokerProfile>(p => p.UserId);
    }
}
