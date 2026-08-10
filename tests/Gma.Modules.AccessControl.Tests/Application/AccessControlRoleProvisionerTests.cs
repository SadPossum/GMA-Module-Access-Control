namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Framework.Permissions;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessControlRoleProvisionerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Contracts_facade_provisions_and_pages_role_assignments()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = new(
            dbContext,
            new SequenceIdGenerator(),
            new ExactScopeMatchOptionsResolver(),
            new FixedClock(Now));
        AccessControlRoleProvisioner provisioner = new(
            repository,
            new FixedClock(Now));
        AccessScope scope = AccessScope.Parse("tenant:tenant-a");
        AccessSubject first = AccessSubject.User("member-a");
        AccessSubject second = AccessSubject.User("member-b");

        await provisioner.EnsureRoleAsync(new AccessControlRoleDefinition(
            "workspace-manager",
            ["properties.read", "properties.read", "reservations.manage"]));
        await provisioner.EnsureAssignmentAsync(first, "workspace-manager", scope);
        await provisioner.EnsureAssignmentAsync(second, "workspace-manager", scope);

        await provisioner.EnsureRoleAsync(new AccessControlRoleDefinition(
            "workspace-manager",
            ["properties.read"]));

        Assert.True(await provisioner.HasAssignmentAsync(first, "workspace-manager", scope));
        Assert.True(await repository.RoleHasPermissionAsync(
            "workspace-manager", "properties.read", CancellationToken.None));
        Assert.False(await repository.RoleHasPermissionAsync(
            "workspace-manager", "reservations.manage", CancellationToken.None));
        AccessControlPage<AccessControlRoleAssignment> firstPage = await provisioner.ListAssignmentsAsync(
            "workspace-manager",
            scope,
            page: 1,
            pageSize: 1);
        AccessControlPage<AccessControlRoleAssignment> secondPage = await provisioner.ListAssignmentsAsync(
            "workspace-manager",
            scope,
            page: 2,
            pageSize: 1);

        AccessControlRoleAssignment firstAssignment = Assert.Single(firstPage.Items);
        AccessControlRoleAssignment secondAssignment = Assert.Single(secondPage.Items);
        Assert.Equal("member-a", firstAssignment.SubjectId);
        Assert.Equal("member-b", secondAssignment.SubjectId);
        Assert.True(firstPage.HasMore);
        Assert.False(secondPage.HasMore);
        Assert.Equal(Now, firstAssignment.CreatedAtUtc);

        Assert.Equal(
            AccessControlAssignmentRemovalOutcome.Removed,
            await provisioner.RemoveAssignmentAsync(first, "workspace-manager", scope));
        Assert.False(await provisioner.HasAssignmentAsync(first, "workspace-manager", scope));
        Assert.Equal(
            AccessControlAssignmentRemovalOutcome.NotFound,
            await provisioner.RemoveAssignmentAsync(first, "workspace-manager", scope));
    }

    [Fact]
    public async Task Contracts_facade_checks_any_normalized_role_in_one_exact_scope()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        AccessControlRbacRepository repository = new(
            dbContext,
            new SequenceIdGenerator(),
            new ExactScopeMatchOptionsResolver(),
            new FixedClock(Now));
        AccessControlRoleProvisioner provisioner = new(repository, new FixedClock(Now));
        AccessSubject subject = AccessSubject.User("member-a");
        AccessScope tenantA = AccessScope.Parse("tenant:tenant-a");
        AccessScope tenantB = AccessScope.Parse("tenant:tenant-b");

        await provisioner.EnsureRoleAsync(new AccessControlRoleDefinition("workspace-member", []));
        await provisioner.EnsureAssignmentAsync(subject, "workspace-member", tenantA);

        Assert.True(await provisioner.HasAnyAssignmentAsync(
            subject,
            ["missing-role", " WORKSPACE-MEMBER ", "workspace-member"],
            tenantA));
        Assert.False(await provisioner.HasAnyAssignmentAsync(
            subject,
            ["workspace-member"],
            tenantB));
        Assert.False(await provisioner.HasAnyAssignmentAsync(subject, [], tenantA));

        Assert.Equal(
            AccessControlAssignmentRemovalOutcome.Removed,
            await provisioner.RemoveAssignmentAsync(subject, "workspace-member", tenantA));
        Assert.False(await provisioner.HasAnyAssignmentAsync(
            subject,
            ["workspace-member"],
            tenantA));
    }

    private static AccessControlDbContext CreateDbContext()
    {
        DbContextOptions<AccessControlDbContext> options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AccessControlDbContext(options);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class ExactScopeMatchOptionsResolver : IAccessScopeMatchOptionsResolver
    {
        public AccessScopeMatchOptions Resolve(PermissionCode permission) => new();
    }

    private sealed class SequenceIdGenerator : IIdGenerator
    {
        private int next;

        public Guid NewId()
        {
            this.next++;
            return Guid.Parse($"00000000-0000-0000-0000-{this.next:000000000000}");
        }
    }
}
