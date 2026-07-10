namespace Gma.Modules.AccessControl.Persistence.Configurations;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class AccessPrincipalConfiguration : IEntityTypeConfiguration<AccessPrincipal>
{
    public void Configure(EntityTypeBuilder<AccessPrincipal> builder)
    {
        builder.ToTable("principals");
        builder.HasKey(principal => new { principal.Kind, principal.SubjectId });
        builder.Property(principal => principal.Kind).IsRequired();
        builder.Property(principal => principal.SubjectId).HasMaxLength(AccessSubject.IdMaxLength).IsRequired();
        builder.Property(principal => principal.CreatedAtUtc).IsRequired();
    }
}
