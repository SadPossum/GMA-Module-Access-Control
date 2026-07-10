namespace Gma.Modules.AccessControl.Persistence.Configurations;

using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class AccessRoleConfiguration : IEntityTypeConfiguration<AccessRole>
{
    public void Configure(EntityTypeBuilder<AccessRole> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(role => role.Id);
        builder.Property(role => role.Name).HasMaxLength(AccessControlRoleName.MaxLength).IsRequired();
        builder.Property(role => role.CreatedAtUtc).IsRequired();
        builder.HasIndex(role => role.Name).IsUnique();

        builder.HasMany(role => role.Permissions)
            .WithOne(permission => permission.Role)
            .HasForeignKey(permission => permission.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(role => role.Permissions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(role => role.Assignments)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
