namespace Gma.Modules.AccessControl.Persistence;

using Gma.Modules.AccessControl.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

public sealed class AccessControlDbContext(DbContextOptions<AccessControlDbContext> options) : DbContext(options)
{
    public DbSet<AccessPrincipal> Principals => this.Set<AccessPrincipal>();
    public DbSet<AccessRole> Roles => this.Set<AccessRole>();
    public DbSet<AccessRolePermission> RolePermissions => this.Set<AccessRolePermission>();
    public DbSet<AccessSubjectRoleAssignment> SubjectRoleAssignments => this.Set<AccessSubjectRoleAssignment>();
    internal DbSet<AccessBootstrapState> BootstrapState => this.Set<AccessBootstrapState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(AccessControlMigrations.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccessControlDbContext).Assembly);
    }
}
