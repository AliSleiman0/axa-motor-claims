using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Claims.Persistence;

public sealed class CachedClaimConfiguration : IEntityTypeConfiguration<CachedClaim>
{
    public void Configure(EntityTypeBuilder<CachedClaim> builder)
    {
        builder.ToTable("claim");

        // Natural key, not a surrogate: §4 keys the cache on visa_no, and the visa number is how
        // every caller already identifies a claim. A generated id would only add a second identity
        // for the same row and a lookup to translate between them.
        builder.HasKey(c => c.VisaNo);
        builder.Property(c => c.VisaNo).HasColumnName("visa_no").HasMaxLength(50).ValueGeneratedNever();
        builder.Property(c => c.PolicyNo).HasColumnName("policy_no").HasMaxLength(50).IsRequired();
        builder.Property(c => c.PlateNo).HasColumnName("plate_no").HasMaxLength(50).IsRequired();
        builder.Property(c => c.InsuredName).HasColumnName("insured_name").HasMaxLength(200).IsRequired();
        builder.Property(c => c.InsuredPhone).HasColumnName("insured_phone").HasMaxLength(32).IsRequired();
        builder.Property(c => c.CarMakeModel).HasColumnName("car_make_model").HasMaxLength(200).IsRequired();
        builder.Property(c => c.City).HasColumnName("city").HasMaxLength(100).IsRequired();
        builder.Property(c => c.AccidentDate).HasColumnName("accident_date");
        builder.Property(c => c.FetchedAt).HasColumnName("fetched_at");

        // insured_phone is deliberately wider than app_user.phone's 16: that column is E.164 we
        // validate, this one is whatever NEXT3 holds, and truncating cached data to fit our rules
        // would corrupt the copy rather than reject it.
    }
}
