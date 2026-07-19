namespace Gma.Modules.AccessControl.Persistence.Configurations;

using Gma.Framework.Permissions;
using Gma.Modules.AccessControl.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class AccessProfilePermissionConfiguration : IEntityTypeConfiguration<AccessProfilePermission>
{
    public void Configure(EntityTypeBuilder<AccessProfilePermission> builder)
    {
        builder.ToTable("access_profile_permissions");
        builder.HasKey(permission => new { permission.ProfileId, permission.PermissionCode });
        builder.Property(permission => permission.PermissionCode).HasMaxLength(PermissionCode.MaxLength).IsRequired();
        builder.Property(permission => permission.CreatedAtUtc).IsRequired();
        builder.HasIndex(permission => permission.PermissionCode);
    }
}
