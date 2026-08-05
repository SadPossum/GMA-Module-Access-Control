namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Pagination;
using Gma.Framework.Permissions;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Handlers;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

[Trait("Category", "Unit")]
public sealed class TemporaryRoleAssignmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly AccessScope TenantScope = AccessScope.Parse("tenant:tenant-a");

    [Fact]
    public async Task Lease_authorizes_before_expiry_and_denies_at_expiry()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        MutableClock clock = new(Now);
        AccessControlRbacRepository repository = CreateRepository(dbContext, clock);
        await SeedRoleAsync(repository, dbContext, "support-reader", "properties.read");
        AssignRoleCommandHandler handler = CreateAssignHandler(repository, clock);
        DateTimeOffset expiresAtUtc = Now.AddHours(1);

        Result<Unit> result = await handler.HandleAsync(
            new AssignRoleCommand(
                AccessSubjectKind.AdminActor,
                "support-a",
                "support-reader",
                TenantScope,
                expiresAtUtc),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(await repository.HasPermissionAsync(
            AccessSubject.AdminActor("support-a"),
            PermissionCode.Create("properties.read"),
            TenantScope,
            CancellationToken.None));

        clock.UtcNow = expiresAtUtc;

        Assert.False(await repository.HasPermissionAsync(
            AccessSubject.AdminActor("support-a"),
            PermissionCode.Create("properties.read"),
            TenantScope,
            CancellationToken.None));
        Assert.Empty((await repository.ListRoleAssignmentsPageAsync(
            "support-reader",
            PageRequest.Normalize(1, 10),
            includeInactive: false,
            CancellationToken.None)).Items);
        AccessControlRoleAssignmentDetails history = Assert.Single((await repository
            .ListRoleAssignmentsPageAsync(
                "support-reader",
                PageRequest.Normalize(1, 10),
                includeInactive: true,
                CancellationToken.None)).Items);
        Assert.Equal(AccessRoleAssignmentStatus.Expired, history.Status);
        Assert.Equal(expiresAtUtc, history.ExpiresAtUtc);
    }

    [Fact]
    public async Task Revocation_retains_history_and_allows_a_new_lease()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        MutableClock clock = new(Now);
        AccessControlRbacRepository repository = CreateRepository(dbContext, clock);
        await SeedRoleAsync(repository, dbContext, "support-reader", "properties.read");
        AssignRoleCommandHandler assign = CreateAssignHandler(repository, clock);
        UnassignRoleCommandHandler unassign = new(repository, clock);
        AccessSubject subject = AccessSubject.AdminActor("support-a");

        Assert.True((await assign.HandleAsync(
            new AssignRoleCommand(
                subject.Kind,
                subject.Id,
                "support-reader",
                TenantScope,
                Now.AddHours(1)),
            CancellationToken.None)).IsSuccess);

        clock.UtcNow = Now.AddMinutes(10);
        Assert.True((await unassign.HandleAsync(
            new UnassignRoleCommand(subject.Kind, subject.Id, "support-reader", TenantScope),
            CancellationToken.None)).IsSuccess);

        clock.UtcNow = Now.AddMinutes(20);
        Assert.True((await assign.HandleAsync(
            new AssignRoleCommand(
                subject.Kind,
                subject.Id,
                "support-reader",
                TenantScope,
                Now.AddHours(2)),
            CancellationToken.None)).IsSuccess);

        AccessControlRoleAssignmentDetails[] history = (await repository.ListRoleAssignmentsPageAsync(
                "support-reader",
                PageRequest.Normalize(1, 10),
                includeInactive: true,
                CancellationToken.None))
            .Items
            .ToArray();
        Assert.Equal(2, history.Length);
        Assert.Contains(history, assignment => assignment.Status == AccessRoleAssignmentStatus.Revoked);
        Assert.Contains(history, assignment => assignment.Status == AccessRoleAssignmentStatus.Active);
        Assert.Equal(2, history.Select(assignment => assignment.Id).Distinct().Count());
        Assert.True(await repository.AssignmentExistsAsync(
            subject,
            "support-reader",
            TenantScope,
            CancellationToken.None));
    }

    [Fact]
    public async Task Temporary_owner_and_rejected_product_policy_fail_closed()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        MutableClock clock = new(Now);
        AccessControlRbacRepository repository = CreateRepository(dbContext, clock);
        await SeedRoleAsync(
            repository,
            dbContext,
            "owner",
            AccessControlPermissionGrant.OwnerWildcard);

        AssignRoleCommandHandler ownerHandler = CreateAssignHandler(repository, clock);
        Result<Unit> owner = await ownerHandler.HandleAsync(
            new AssignRoleCommand(
                AccessSubjectKind.AdminActor,
                "temporary-owner",
                "owner",
                AccessScope.Global,
                Now.AddHours(1)),
            CancellationToken.None);
        Assert.Equal(
            AccessControlApplicationErrors.TemporaryOwnerAssignmentNotAllowed,
            owner.Error);

        await SeedRoleAsync(repository, dbContext, "support-reader", "properties.read");
        AssignRoleCommandHandler deniedHandler = new(
            repository,
            AccessControlTestAdmissions.AllowAll(),
            new AccessRoleAssignmentPolicy([new DenyAllAssignmentsPolicy()]),
            clock);
        Result<Unit> denied = await deniedHandler.HandleAsync(
            new AssignRoleCommand(
                AccessSubjectKind.AdminActor,
                "support-a",
                "support-reader",
                TenantScope,
                Now.AddHours(1)),
            CancellationToken.None);

        Assert.Equal(AccessControlApplicationErrors.AssignmentRejected, denied.Error);
        Assert.False(await repository.AssignmentExistsAsync(
            AccessSubject.AdminActor("support-a"),
            "support-reader",
            TenantScope,
            CancellationToken.None));
    }

    [Fact]
    public async Task Assignment_policy_receives_the_current_role_permissions()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        MutableClock clock = new(Now);
        AccessControlRbacRepository repository = CreateRepository(dbContext, clock);
        await SeedRoleAsync(repository, dbContext, "support-reader", "properties.read");
        CapturingAssignmentPolicy policy = new();
        AssignRoleCommandHandler handler = new(
            repository,
            AccessControlTestAdmissions.AllowAll(),
            new AccessRoleAssignmentPolicy([policy]),
            clock);

        Result<Unit> result = await handler.HandleAsync(
            new AssignRoleCommand(
                AccessSubjectKind.AdminActor,
                "support-a",
                "support-reader",
                TenantScope,
                Now.AddHours(1)),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["properties.read"], policy.Permissions);
    }

    [Fact]
    public async Task Changed_role_definition_invalidates_the_assignment_snapshot()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        MutableClock clock = new(Now);
        AccessControlRbacRepository repository = CreateRepository(dbContext, clock);
        await SeedRoleAsync(repository, dbContext, "support-reader", "properties.read");
        string[] expectedPermissions = Assert.IsType<string[]>(
            await repository.FindRolePermissionsAsync("support-reader", CancellationToken.None));
        await repository.EnsureRolePermissionAsync(
            "support-reader",
            "reservations.read",
            Now,
            CancellationToken.None);

        AccessControlRoleAssignmentPersistenceOutcome outcome = await repository.TryAssignRoleAsync(
            AccessSubject.AdminActor("support-a"),
            "support-reader",
            TenantScope,
            Now,
            Now.AddHours(1),
            expectedPermissions,
            CancellationToken.None);

        Assert.Equal(
            AccessControlRoleAssignmentPersistenceOutcome.RoleDefinitionChanged,
            outcome);
        Assert.False(await repository.AssignmentExistsAsync(
            AccessSubject.AdminActor("support-a"),
            "support-reader",
            TenantScope,
            CancellationToken.None));
    }

    [Fact]
    public async Task Lease_expiry_must_survive_utc_millisecond_normalization()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        MutableClock clock = new(Now.AddTicks(500));
        AccessControlRbacRepository repository = CreateRepository(dbContext, clock);
        await SeedRoleAsync(repository, dbContext, "support-reader", "properties.read");
        AssignRoleCommandHandler handler = CreateAssignHandler(repository, clock);

        Result<Unit> result = await handler.HandleAsync(
            new AssignRoleCommand(
                AccessSubjectKind.AdminActor,
                "support-a",
                "support-reader",
                TenantScope,
                clock.UtcNow.AddTicks(1)),
            CancellationToken.None);

        Assert.Equal(AccessControlApplicationErrors.AssignmentExpiryInvalid, result.Error);
    }

    [Fact]
    public async Task Role_permissions_cannot_expand_after_a_temporary_assignment()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        MutableClock clock = new(Now);
        AccessControlRbacRepository repository = CreateRepository(dbContext, clock);
        await SeedRoleAsync(repository, dbContext, "support-reader", "properties.read");
        AssignRoleCommandHandler assign = CreateAssignHandler(repository, clock);
        Assert.True((await assign.HandleAsync(
            new AssignRoleCommand(
                AccessSubjectKind.AdminActor,
                "support-a",
                "support-reader",
                TenantScope,
                Now.AddHours(1)),
            CancellationToken.None)).IsSuccess);

        GrantRolePermissionCommandHandler grant = new(repository, clock);
        Result<Unit> result = await grant.HandleAsync(
            new GrantRolePermissionCommand(
                "support-reader",
                "reservations.read"),
            CancellationToken.None);

        Assert.Equal(
            AccessControlApplicationErrors.RolePermissionExpansionTemporaryAssignmentsExist,
            result.Error);
        Assert.False(await repository.RoleHasPermissionAsync(
            "support-reader",
            "reservations.read",
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.EnsureRoleDefinitionAsync(
                "support-reader",
                ["properties.read", "reservations.read"],
                Now,
                CancellationToken.None));
    }

    [Fact]
    public async Task Role_permissions_can_expand_after_all_temporary_assignments_expire()
    {
        await using AccessControlDbContext dbContext = CreateDbContext();
        MutableClock clock = new(Now);
        AccessControlRbacRepository repository = CreateRepository(dbContext, clock);
        await SeedRoleAsync(repository, dbContext, "support-reader", "properties.read");
        AssignRoleCommandHandler assign = CreateAssignHandler(repository, clock);
        Assert.True((await assign.HandleAsync(
            new AssignRoleCommand(
                AccessSubjectKind.AdminActor,
                "support-a",
                "support-reader",
                TenantScope,
                Now.AddHours(1)),
            CancellationToken.None)).IsSuccess);
        clock.UtcNow = Now.AddHours(1);

        Result<Unit> result = await new GrantRolePermissionCommandHandler(repository, clock)
            .HandleAsync(
                new GrantRolePermissionCommand("support-reader", "reservations.read"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(await repository.RoleHasPermissionAsync(
            "support-reader",
            "reservations.read",
            CancellationToken.None));
    }

    private static AssignRoleCommandHandler CreateAssignHandler(
        AccessControlRbacRepository repository,
        ISystemClock clock) =>
        new(
            repository,
            AccessControlTestAdmissions.AllowAll(),
            new AccessRoleAssignmentPolicy([]),
            clock);

    private static async Task SeedRoleAsync(
        AccessControlRbacRepository repository,
        AccessControlDbContext dbContext,
        string roleName,
        string permission)
    {
        await repository.EnsureRoleAsync(roleName, Now, CancellationToken.None);
        await repository.EnsureRolePermissionAsync(roleName, permission, Now, CancellationToken.None);
        await dbContext.SaveChangesAsync();
    }

    private static AccessControlRbacRepository CreateRepository(
        AccessControlDbContext dbContext,
        ISystemClock clock) =>
        new(
            dbContext,
            new SequenceIdGenerator(),
            new ExactScopeMatchOptionsResolver(),
            clock);

    private static AccessControlDbContext CreateDbContext()
    {
        DbContextOptions<AccessControlDbContext> options =
            new DbContextOptionsBuilder<AccessControlDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
        return new AccessControlDbContext(options);
    }

    private sealed class DenyAllAssignmentsPolicy : IAccessRoleAssignmentPolicy
    {
        public ValueTask<bool> IsAllowedAsync(
            AccessRoleAssignmentPolicyContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }

    private sealed class CapturingAssignmentPolicy : IAccessRoleAssignmentPolicy
    {
        public IReadOnlyList<string> Permissions { get; private set; } = [];

        public ValueTask<bool> IsAllowedAsync(
            AccessRoleAssignmentPolicyContext context,
            CancellationToken cancellationToken = default)
        {
            this.Permissions = context.Permissions;
            return ValueTask.FromResult(true);
        }
    }

    private sealed class ExactScopeMatchOptionsResolver : IAccessScopeMatchOptionsResolver
    {
        public AccessScopeMatchOptions Resolve(PermissionCode permission) => new();
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
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
