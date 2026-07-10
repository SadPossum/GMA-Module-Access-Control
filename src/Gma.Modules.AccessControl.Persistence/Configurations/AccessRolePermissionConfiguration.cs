namespace Gma.Modules.AccessControl.Persistence.Configurations;

using Gma.Framework.AccessControl;
using Gma.Framework.Permissions;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class AccessRolePermissionConfiguration : IEntityTypeConfiguration<AccessRolePermission>
{
    public void Configure(EntityTypeBuilder<AccessRolePermission> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(permission => permission.Id);
        builder.Property(permission => permission.PermissionCode)
            .HasMaxLength(PermissionCode.MaxLength)
            .IsRequired();
        builder.Property(permission => permission.CreatedAtUtc).IsRequired();
        builder.HasIndex(permission => new { permission.RoleId, permission.PermissionCode }).IsUnique();
    }
}
