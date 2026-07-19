namespace Gma.Modules.AccessControl.Persistence.Configurations;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class AccessProfileConfiguration : IEntityTypeConfiguration<AccessProfile>
{
    public void Configure(EntityTypeBuilder<AccessProfile> builder)
    {
        builder.ToTable("access_profiles");
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Id).ValueGeneratedNever();
        builder.Ignore(profile => profile.OwnerScope);
        builder.Property(profile => profile.OwnerScopeValue)
            .HasColumnName("OwnerScope")
            .HasMaxLength(AccessScope.MaxLength)
            .IsRequired();
        builder.Property(profile => profile.Key).HasMaxLength(AccessProfileKey.MaxLength).IsRequired();
        builder.Property(profile => profile.DisplayName).HasMaxLength(AccessProfileDisplayName.MaxLength).IsRequired();
        builder.Property(profile => profile.Description).HasMaxLength(AccessProfileDescription.MaxLength).IsRequired();
        builder.Property(profile => profile.Status).HasConversion<int>().IsRequired();
        builder.Property(profile => profile.Version).IsConcurrencyToken();
        builder.Property(profile => profile.CreatedByKind).IsRequired();
        builder.Property(profile => profile.CreatedById).HasMaxLength(AccessSubject.IdMaxLength).IsRequired();
        builder.Property(profile => profile.CreatedAtUtc).IsRequired();
        builder.Property(profile => profile.LastChangedByKind).IsRequired();
        builder.Property(profile => profile.LastChangedById).HasMaxLength(AccessSubject.IdMaxLength).IsRequired();
        builder.Property(profile => profile.LastChangedAtUtc).IsRequired();
        builder.HasIndex(profile => new { profile.OwnerScopeValue, profile.Key }).IsUnique();
        builder.HasIndex(profile => new { profile.OwnerScopeValue, profile.Status, profile.Key });

        builder.HasMany(profile => profile.Permissions)
            .WithOne(permission => permission.Profile)
            .HasForeignKey(permission => permission.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(profile => profile.Changes)
            .WithOne(change => change.Profile)
            .HasForeignKey(change => change.ProfileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(profile => profile.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(profile => profile.Changes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
