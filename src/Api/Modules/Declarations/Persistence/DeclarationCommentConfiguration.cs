using Api.Modules.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Api.Modules.Declarations.Persistence;

public sealed class DeclarationCommentConfiguration : IEntityTypeConfiguration<DeclarationComment>
{
    public void Configure(EntityTypeBuilder<DeclarationComment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("declaration_comment");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(c => c.DeclarationId).HasColumnName("declaration_id");
        builder.Property(c => c.AuthorUserId).HasColumnName("author_user_id");
        builder.Property(c => c.Body).HasColumnName("body").HasMaxLength(2000).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");

        // Cascade here, unlike every other FK in the model: a comment has no meaning apart from the
        // declaration it is about, so an orphan would be unreadable rather than merely unreferenced.
        // There is no hard-delete path for a declaration today either.
        builder.HasOne<Declaration>().WithMany()
            .HasForeignKey(c => c.DeclarationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict on the author, as everywhere else: §9's audit answer to "who rejected this claim,
        // and what did they say" must survive a user being removed.
        builder.HasOne<AppUser>().WithMany()
            .HasForeignKey(c => c.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Both views read a declaration's comments in the order they were written.
        builder.HasIndex(c => new { c.DeclarationId, c.CreatedAt });
    }
}
