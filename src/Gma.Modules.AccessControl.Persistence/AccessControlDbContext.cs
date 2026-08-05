namespace Gma.Modules.AccessControl.Persistence;

using Gma.Framework.Messaging.Infrastructure;
using Gma.Modules.AccessControl.Persistence.Entities;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;
using Microsoft.EntityFrameworkCore;

public sealed partial class AccessControlDbContext(DbContextOptions<AccessControlDbContext> options) : DbContext(options)
{
    public DbSet<AccessPrincipal> Principals => this.Set<AccessPrincipal>();
    public DbSet<AccessRole> Roles => this.Set<AccessRole>();
    public DbSet<AccessRolePermission> RolePermissions => this.Set<AccessRolePermission>();
    public DbSet<AccessSubjectRoleAssignment> SubjectRoleAssignments => this.Set<AccessSubjectRoleAssignment>();
    public DbSet<AccessProfile> AccessProfiles => this.Set<AccessProfile>();
    public DbSet<AccessProfilePermission> AccessProfilePermissions => this.Set<AccessProfilePermission>();
    public DbSet<AccessProfileAssignment> AccessProfileAssignments => this.Set<AccessProfileAssignment>();
    public DbSet<AccessProfileChange> AccessProfileChanges => this.Set<AccessProfileChange>();
    public DbSet<InboxMessage> InboxMessages => this.Set<InboxMessage>();
    internal DbSet<AccessBootstrapState> BootstrapState => this.Set<AccessBootstrapState>();
    internal DbSet<AccessControlScopeState> AccessScopeStates =>
        this.Set<AccessControlScopeState>();
    internal DbSet<AccessControlScopeDestroyOperation>
        AccessScopeDestroyOperations =>
        this.Set<AccessControlScopeDestroyOperation>();
    internal DbSet<AccessControlScopeDestroyReceipt>
        AccessScopeDestroyReceipts =>
        this.Set<AccessControlScopeDestroyReceipt>();
    internal DbSet<AccessControlScopeDestroyPrincipalCandidate>
        AccessScopeDestroyPrincipalCandidates =>
        this.Set<AccessControlScopeDestroyPrincipalCandidate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(AccessControlMigrations.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccessControlDbContext).Assembly);
    }
}
