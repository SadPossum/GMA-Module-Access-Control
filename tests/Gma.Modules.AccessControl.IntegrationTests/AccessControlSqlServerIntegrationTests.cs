namespace Gma.Modules.AccessControl.IntegrationTests;

using Gma.Framework.AccessControl;
using Gma.Framework.Permissions;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Handlers;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.Domain.ValueObjects;
using Gma.Modules.AccessControl.IntegrationTests.Support;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Entities;
using Gma.Modules.AccessControl.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.MsSql;
using Xunit;
using DomainChangeKind = Gma.Modules.AccessControl.Domain.Enums.AccessProfileChangeKind;

[Trait("Category", "Docker")]
[Trait("Category", "Integration")]
public sealed class AccessControlSqlServerIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly AccessScope TenantScope = AccessScope.Parse("tenant:tenant-a");
    private static readonly AccessProfileSubject Actor = new(AccessProfileSubjectKind.System, "integration-tests");

    [DockerFact]
    public async Task SqlServer_schema_upgrade_backfills_existing_profile_and_role_assignment_scopes()
    {
        await using MsSqlContainer sqlServer = CreateSqlServer();
        await sqlServer.StartAsync();
        string connectionString = sqlServer.GetConnectionString();
        Guid profileId = Guid.NewGuid();
        Guid assignmentId = Guid.NewGuid();
        Guid roleId = Guid.NewGuid();
        Guid roleAssignmentId = Guid.NewGuid();
        const string subjectId = "legacy-user";
        const string profileKey = "legacy-profile";
        const string profileName = "Legacy profile";
        const string roleName = "legacy-role";
        const string actorId = "migration-test";

        await using (AccessControlDbContext legacy = CreateDbContext(connectionString))
        {
            IMigrator migrator = legacy.GetService<IMigrator>();
            await migrator.MigrateAsync("20260719070235_AddScopedAccessProfiles");
            await legacy.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO [access].[principals] ([Kind], [SubjectId], [CreatedAtUtc])
                VALUES ({(int)AccessProfileSubjectKind.User}, {subjectId}, {Now});

                INSERT INTO [access].[access_profiles]
                    ([Id], [OwnerScope], [Key], [DisplayName], [Description], [Status], [Version],
                     [CreatedByKind], [CreatedById], [CreatedAtUtc], [LastChangedByKind], [LastChangedById], [LastChangedAtUtc])
                VALUES
                    ({profileId}, {TenantScope.Value}, {profileKey}, {profileName}, {string.Empty},
                     {(int)Gma.Modules.AccessControl.Domain.Enums.AccessProfileStatus.Active}, {1L},
                     {(int)AccessProfileSubjectKind.System}, {actorId},
                     {Now}, {(int)AccessProfileSubjectKind.System}, {actorId}, {Now});

                INSERT INTO [access].[access_profile_assignments]
                    ([Id], [ProfileId], [SubjectKind], [SubjectId], [CreatedByKind], [CreatedById], [CreatedAtUtc])
                VALUES
                    ({assignmentId}, {profileId}, {(int)AccessProfileSubjectKind.User}, {subjectId},
                     {(int)AccessProfileSubjectKind.System}, {actorId}, {Now});

                INSERT INTO [access].[roles] ([Id], [Name], [CreatedAtUtc])
                VALUES ({roleId}, {roleName}, {Now});

                INSERT INTO [access].[subject_role_assignments]
                    ([Id], [SubjectKind], [SubjectId], [RoleId], [Scope], [CreatedAtUtc])
                VALUES
                    ({roleAssignmentId}, {(int)AccessSubjectKind.User}, {subjectId},
                     {roleId}, {TenantScope.Value}, {Now});
                """);
            await legacy.Database.MigrateAsync();
        }

        await using AccessControlDbContext verification = CreateDbContext(connectionString);
        AccessProfileAssignment assignment = await verification.AccessProfileAssignments.SingleAsync();
        Assert.Equal(TenantScope.Value, assignment.AssignmentScopeValue);
        AccessSubjectRoleAssignment roleAssignment =
            await verification.SubjectRoleAssignments.SingleAsync();
        Assert.Equal(64, roleAssignment.ScopeHash.Length);
        Assert.True(await CreateRbacRepository(verification).AssignmentExistsAsync(
            AccessSubject.User(subjectId),
            roleName,
            TenantScope,
            CancellationToken.None));
    }

    [DockerFact]
    public async Task SqlServer_concurrent_bootstrap_has_exactly_one_winner()
    {
        await using MsSqlContainer sqlServer = CreateSqlServer();
        await sqlServer.StartAsync();
        string connectionString = sqlServer.GetConnectionString();
        await MigrateAsync(connectionString);

        bool[] outcomes = await Task.WhenAll(
            BootstrapAsync(connectionString, "owner-a"),
            BootstrapAsync(connectionString, "owner-b"));

        Assert.Single(outcomes, outcome => outcome);
        Assert.Single(outcomes, outcome => !outcome);
        await using AccessControlDbContext verification = CreateDbContext(connectionString);
        Assert.Single(await verification.SubjectRoleAssignments.ToArrayAsync());
    }

    [DockerFact]
    public async Task SqlServer_concurrent_final_owner_removals_leave_one_protected_owner()
    {
        await using MsSqlContainer sqlServer = CreateSqlServer();
        await sqlServer.StartAsync();
        string connectionString = sqlServer.GetConnectionString();
        await MigrateAsync(connectionString);
        AccessSubject ownerA = AccessSubject.AdminActor("owner-a");
        AccessSubject ownerB = AccessSubject.AdminActor("owner-b");
        await using (AccessControlDbContext seed = CreateDbContext(connectionString))
        {
            AccessControlRbacRepository repository = CreateRbacRepository(seed);
            await repository.EnsureSubjectAsync(ownerA, Now, CancellationToken.None);
            await repository.EnsureSubjectAsync(ownerB, Now, CancellationToken.None);
            await repository.EnsureRoleAsync("owner", Now, CancellationToken.None);
            await repository.EnsureRolePermissionAsync(
                "owner", AccessControlPermissionGrants.OwnerWildcard, Now, CancellationToken.None);
            await repository.EnsureRoleAssignmentAsync(
                ownerA, "owner", AccessScope.Global, Now, CancellationToken.None);
            await repository.EnsureRoleAssignmentAsync(
                ownerB, "owner", AccessScope.Global, Now, CancellationToken.None);
            await seed.SaveChangesAsync();
        }

        AccessControlRemovalOutcome[] outcomes = await Task.WhenAll(
            RemoveOwnerAsync(connectionString, ownerA),
            RemoveOwnerAsync(connectionString, ownerB));

        Assert.Contains(AccessControlRemovalOutcome.Removed, outcomes);
        Assert.Contains(AccessControlRemovalOutcome.LastOwnerProtected, outcomes);
        await using AccessControlDbContext verification = CreateDbContext(connectionString);
        AccessSubjectRoleAssignment[] assignments =
            await verification.SubjectRoleAssignments.ToArrayAsync();
        Assert.Equal(2, assignments.Length);
        Assert.Single(assignments, assignment => assignment.IsActiveAt(Now));
        Assert.Single(assignments, assignment => assignment.RevokedAtUtc is not null);
    }

    [DockerFact]
    public async Task SqlServer_temporary_role_lease_is_atomic_and_expiry_aware()
    {
        await using MsSqlContainer sqlServer = CreateSqlServer();
        await sqlServer.StartAsync();
        string connectionString = sqlServer.GetConnectionString();
        await MigrateAsync(connectionString);
        DateTimeOffset expiresAtUtc = Now.AddMinutes(30);
        const string roleName = "support-reader";
        AccessSubject subject = AccessSubject.AdminActor("support-a");

        string[] expectedPermissions;
        await using (AccessControlDbContext seed = CreateDbContext(connectionString))
        {
            AccessControlRbacRepository repository = CreateRbacRepository(seed);
            await repository.EnsureRoleDefinitionAsync(
                roleName,
                ["properties.read"],
                Now,
                CancellationToken.None);
            expectedPermissions = Assert.IsType<string[]>(
                await repository.FindRolePermissionsAsync(roleName, CancellationToken.None));
        }

        AccessControlRoleAssignmentPersistenceOutcome[] outcomes = await Task.WhenAll(
            TryAssignTemporaryRoleAsync(
                connectionString,
                subject,
                roleName,
                expiresAtUtc,
                expectedPermissions),
            TryAssignTemporaryRoleAsync(
                connectionString,
                subject,
                roleName,
                expiresAtUtc,
                expectedPermissions));
        Assert.Single(
            outcomes,
            outcome => outcome == AccessControlRoleAssignmentPersistenceOutcome.Assigned);
        Assert.Single(
            outcomes,
            outcome => outcome == AccessControlRoleAssignmentPersistenceOutcome.AlreadyExists);

        await using (AccessControlDbContext activeContext = CreateDbContext(connectionString))
        {
            AccessControlRbacRepository active = CreateRbacRepository(activeContext, Now);
            AccessSubjectRoleAssignment persisted = Assert.Single(
                await activeContext.SubjectRoleAssignments.AsNoTracking().ToArrayAsync());
            Assert.Equal(64, persisted.ScopeHash.Length);
            Assert.DoesNotContain(TenantScope.Value, persisted.ScopeHash, StringComparison.Ordinal);
            Assert.True(await active.HasPermissionAsync(
                subject,
                PermissionCode.Create("properties.read"),
                TenantScope,
                CancellationToken.None));
        }

        await using AccessControlDbContext expiredContext = CreateDbContext(connectionString);
        AccessControlRbacRepository expired = CreateRbacRepository(expiredContext, expiresAtUtc);
        Assert.False(await expired.HasPermissionAsync(
            subject,
            PermissionCode.Create("properties.read"),
            TenantScope,
            CancellationToken.None));
        Assert.Empty((await expired.ListRoleAssignmentsPageAsync(
            roleName,
            Gma.Framework.Pagination.PageRequest.Normalize(1, 10),
            includeInactive: false,
            CancellationToken.None)).Items);
        AccessControlRoleAssignmentDetails history = Assert.Single((await expired
            .ListRoleAssignmentsPageAsync(
                roleName,
                Gma.Framework.Pagination.PageRequest.Normalize(1, 10),
                includeInactive: true,
                CancellationToken.None)).Items);
        Assert.Equal(AccessRoleAssignmentStatus.Expired, history.Status);

        await using AccessControlDbContext elapsedContext = CreateDbContext(connectionString);
        AccessControlRoleAssignmentPersistenceOutcome elapsed = await CreateRbacRepository(
                elapsedContext,
                expiresAtUtc)
            .TryAssignRoleAsync(
                AccessSubject.AdminActor("support-expired"),
                roleName,
                TenantScope,
                expiresAtUtc,
                expectedPermissions,
                CancellationToken.None);
        Assert.Equal(AccessControlRoleAssignmentPersistenceOutcome.ExpiryElapsed, elapsed);
        Assert.False(await elapsedContext.SubjectRoleAssignments.AnyAsync(assignment =>
            assignment.SubjectId == "support-expired"));
    }

    [DockerFact]
    public async Task SqlServer_transactional_commands_are_serialized_by_the_management_lock()
    {
        await using MsSqlContainer sqlServer = CreateSqlServer();
        await sqlServer.StartAsync();
        string connectionString = sqlServer.GetConnectionString();
        await MigrateAsync(connectionString);

        await AssertTransactionalCommandsSerializeAsync(connectionString);
        await AssertBootstrapUsesCurrentTransactionAsync(connectionString);
    }

    [DockerFact]
    public async Task SqlServer_scoped_profiles_batch_revoke_and_enforce_concurrency_constraints()
    {
        await using MsSqlContainer sqlServer = CreateSqlServer();
        await sqlServer.StartAsync();
        string connectionString = sqlServer.GetConnectionString();
        await MigrateAsync(connectionString);
        AccessSubject subject = AccessSubject.User("user-a");
        AccessProfile profile = CreateProfile("front-desk", ["reservations.read", "guests.read"]);
        await using (AccessControlDbContext seed = CreateDbContext(connectionString))
        {
            AccessControlRbacRepository rbac = CreateRbacRepository(seed);
            await rbac.EnsureSubjectAsync(subject, Now, CancellationToken.None);
            seed.AccessProfiles.Add(profile);
            seed.AccessProfileAssignments.Add(CreateAssignment(profile.Id, subject));
            await seed.SaveChangesAsync();
        }

        await using (AccessControlDbContext reader = CreateDbContext(connectionString))
        {
            IReadOnlyList<bool> decisions = await CreateRbacRepository(reader).HasPermissionsAsync(
            [
                new AccessRequirement(subject, PermissionCode.Create("reservations.read"), TenantScope),
                new AccessRequirement(subject, PermissionCode.Create("guests.read"), TenantScope),
                new AccessRequirement(subject, PermissionCode.Create("inventory.read"), TenantScope)
            ], CancellationToken.None);
            Assert.Equal([true, true, false], decisions);
        }

        await using (AccessControlDbContext writer = CreateDbContext(connectionString))
        {
            RevokeAccessProfileAssignmentsCommandHandler revoker = new(
                new AccessProfileRepository(writer),
                new RandomIdGenerator(),
                new FixedClock(Now.AddMinutes(1)));
            Result<int> revoked = await revoker.HandleAsync(
                new RevokeAccessProfileAssignmentsCommand(
                    subject,
                    TenantScope,
                    AccessSubject.System("membership-sync")),
                CancellationToken.None);
            Assert.True(revoked.IsSuccess);
            Assert.Equal(1, revoked.Value);
            await writer.SaveChangesAsync();
        }

        await using (AccessControlDbContext verification = CreateDbContext(connectionString))
        {
            Assert.Empty(await verification.AccessProfileAssignments.ToArrayAsync());
            Assert.Single(await verification.AccessProfileChanges.Where(change =>
                change.Kind == DomainChangeKind.Unassigned).ToArrayAsync());
        }

        await AssertOptimisticUpdateAsync(connectionString);
        await AssertDuplicateKeyAsync(connectionString);
    }

    private static async Task AssertOptimisticUpdateAsync(string connectionString)
    {
        AccessProfile profile = CreateProfile("night-audit", ["reservations.read"]);
        await using (AccessControlDbContext seed = CreateDbContext(connectionString))
        {
            seed.AccessProfiles.Add(profile);
            await seed.SaveChangesAsync();
        }

        await using AccessControlDbContext first = CreateDbContext(connectionString);
        await using AccessControlDbContext second = CreateDbContext(connectionString);
        AccessProfile firstProfile = await first.AccessProfiles.Include(candidate => candidate.Permissions)
            .SingleAsync(candidate => candidate.Id == profile.Id);
        AccessProfile secondProfile = await second.AccessProfiles.Include(candidate => candidate.Permissions)
            .SingleAsync(candidate => candidate.Id == profile.Id);
        Assert.True(firstProfile.Update(
            "Night audit A", null, ["reservations.read"], 1,
            Actor, Guid.NewGuid(), Now.AddMinutes(2)).IsSuccess);
        Assert.True(secondProfile.Update(
            "Night audit B", null, ["reservations.read"], 1,
            Actor, Guid.NewGuid(), Now.AddMinutes(2)).IsSuccess);

        Exception?[] failures = await Task.WhenAll(
            CaptureAsync(() => first.SaveChangesAsync()),
            CaptureAsync(() => second.SaveChangesAsync()));
        Assert.Single(failures, failure => failure is null);
        Assert.Single(failures, failure => failure is DbUpdateConcurrencyException);
    }

    private static async Task AssertDuplicateKeyAsync(string connectionString)
    {
        await using AccessControlDbContext first = CreateDbContext(connectionString);
        await using AccessControlDbContext second = CreateDbContext(connectionString);
        first.AccessProfiles.Add(CreateProfile("duplicate", []));
        second.AccessProfiles.Add(CreateProfile("duplicate", []));

        Exception?[] failures = await Task.WhenAll(
            CaptureAsync(() => first.SaveChangesAsync()),
            CaptureAsync(() => second.SaveChangesAsync()));
        Assert.Single(failures, failure => failure is null);
        Assert.Single(failures, failure => failure is DbUpdateException);
    }

    private static async Task<bool> BootstrapAsync(string connectionString, string actorId)
    {
        await using AccessControlDbContext dbContext = CreateDbContext(connectionString);
        return await CreateRbacRepository(dbContext).TryBootstrapOwnerAsync(
            AccessSubject.AdminActor(actorId),
            "owner",
            Now,
            allowWhenAssignmentsExist: false,
            CancellationToken.None);
    }

    private static async Task<AccessControlRemovalOutcome> RemoveOwnerAsync(
        string connectionString,
        AccessSubject owner)
    {
        await using AccessControlDbContext dbContext = CreateDbContext(connectionString);
        return await CreateRbacRepository(dbContext).UnassignRoleAsync(
            owner,
            "owner",
            AccessScope.Global,
            CancellationToken.None);
    }

    private static async Task MigrateAsync(string connectionString)
    {
        await using AccessControlDbContext dbContext = CreateDbContext(connectionString);
        await dbContext.Database.MigrateAsync();
    }

    private static async Task AssertTransactionalCommandsSerializeAsync(string connectionString)
    {
        await using AccessControlDbContext firstContext = CreateDbContext(connectionString);
        await using AccessControlDbContext secondContext = CreateDbContext(connectionString);
        AccessControlUnitOfWork first = new(firstContext);
        AccessControlUnitOfWork second = new(secondContext);

        await first.BeginTransactionAsync();
        Task secondLock = second.BeginTransactionAsync();
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            Assert.False(secondLock.IsCompleted);

            await first.CommitTransactionAsync();
            await secondLock.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await first.RollbackTransactionAsync();
            await second.RollbackTransactionAsync();
        }
    }

    private static async Task AssertBootstrapUsesCurrentTransactionAsync(string connectionString)
    {
        await using AccessControlDbContext dbContext = CreateDbContext(connectionString);
        AccessControlUnitOfWork unitOfWork = new(dbContext);
        await unitOfWork.BeginTransactionAsync();
        try
        {
            bool bootstrapped = await CreateRbacRepository(dbContext).TryBootstrapOwnerAsync(
                AccessSubject.AdminActor("transaction-owner"),
                "owner",
                Now,
                allowWhenAssignmentsExist: false,
                CancellationToken.None);

            Assert.True(bootstrapped);
            await unitOfWork.CommitTransactionAsync();
        }
        finally
        {
            await unitOfWork.RollbackTransactionAsync();
        }
    }

    private static AccessControlDbContext CreateDbContext(string connectionString)
    {
        DbContextOptions<AccessControlDbContext> options =
            new DbContextOptionsBuilder<AccessControlDbContext>()
                .UseSqlServer(connectionString, provider => provider
                    .MigrationsAssembly(AccessControlMigrations.SqlServerAssembly)
                    .MigrationsHistoryTable(
                        AccessControlMigrations.HistoryTable,
                        AccessControlMigrations.Schema))
                .Options;
        return new AccessControlDbContext(options);
    }

    private static AccessControlRbacRepository CreateRbacRepository(AccessControlDbContext dbContext) =>
        CreateRbacRepository(dbContext, Now);

    private static AccessControlRbacRepository CreateRbacRepository(
        AccessControlDbContext dbContext,
        DateTimeOffset nowUtc) =>
        new(
            dbContext,
            new RandomIdGenerator(),
            new DefaultScopeMatchOptionsResolver(),
            new FixedClock(nowUtc));

    private static async Task<AccessControlRoleAssignmentPersistenceOutcome> TryAssignTemporaryRoleAsync(
        string connectionString,
        AccessSubject subject,
        string roleName,
        DateTimeOffset expiresAtUtc,
        IReadOnlyCollection<string> expectedPermissions)
    {
        await using AccessControlDbContext dbContext = CreateDbContext(connectionString);
        return await CreateRbacRepository(dbContext).TryAssignRoleAsync(
            subject,
            roleName,
            TenantScope,
            expiresAtUtc,
            expectedPermissions,
            CancellationToken.None);
    }

    private static AccessProfile CreateProfile(string key, IReadOnlyCollection<string> permissions)
    {
        Result<AccessProfile> result = AccessProfile.Create(
            Guid.NewGuid(), TenantScope.Value, key, key, null,
            permissions, Actor, Guid.NewGuid(), Now);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static AccessProfileAssignment CreateAssignment(Guid profileId, AccessSubject subject)
    {
        Result<AccessProfileAssignment> result = AccessProfileAssignment.Create(
            Guid.NewGuid(),
            profileId,
            TenantScope.Value,
            new AccessProfileSubject(AccessProfileSubjectKind.User, subject.Id),
            Actor,
            Now);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static MsSqlContainer CreateSqlServer() =>
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class RandomIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }

    private sealed class FixedClock(DateTimeOffset nowUtc) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = nowUtc;
    }

    private sealed class DefaultScopeMatchOptionsResolver : IAccessScopeMatchOptionsResolver
    {
        public AccessScopeMatchOptions Resolve(PermissionCode permission) => new();
    }
}
