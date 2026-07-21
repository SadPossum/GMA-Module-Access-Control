namespace Gma.Modules.AccessControl.IntegrationTests;

using System.Data.Common;
using Gma.Framework.AccessControl;
using Gma.Framework.Pagination;
using Gma.Framework.Permissions;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.Domain.ValueObjects;
using Gma.Modules.AccessControl.IntegrationTests.Support;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Xunit;
using ContractChangeKind = Gma.Modules.AccessControl.Contracts.AccessProfileChangeKind;
using DomainChangeKind = Gma.Modules.AccessControl.Domain.Enums.AccessProfileChangeKind;

[Trait("Category", "Docker")]
[Trait("Category", "Integration")]
public sealed class AccessControlPostgreSqlIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);
    private static readonly AccessScope TenantScope = AccessScope.Parse("tenant:tenant-a");
    private static readonly AccessProfileSubject Actor = new(AccessProfileSubjectKind.AdminActor, "actor-a");

    [DockerFact]
    public async Task Scoped_assignment_migration_backfills_existing_grants_to_the_profile_owner_scope()
    {
        await using PostgreSqlContainer postgreSql = CreatePostgreSql("access_assignment_migration_tests");
        await postgreSql.StartAsync();
        string connectionString = postgreSql.GetConnectionString();
        Guid profileId = Guid.NewGuid();
        Guid assignmentId = Guid.NewGuid();
        const string subjectId = "legacy-user";
        const string profileKey = "legacy-profile";
        const string profileName = "Legacy profile";
        const string actorId = "migration-test";

        await using (AccessControlDbContext legacy = CreateDbContext(connectionString))
        {
            IMigrator migrator = legacy.GetService<IMigrator>();
            await migrator.MigrateAsync("20260719070242_AddScopedAccessProfiles");
            await legacy.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO access.principals ("Kind", "SubjectId", "CreatedAtUtc")
                VALUES ({(int)AccessProfileSubjectKind.User}, {subjectId}, {Now});

                INSERT INTO access.access_profiles
                    ("Id", "OwnerScope", "Key", "DisplayName", "Description", "Status", "Version",
                     "CreatedByKind", "CreatedById", "CreatedAtUtc", "LastChangedByKind", "LastChangedById", "LastChangedAtUtc")
                VALUES
                    ({profileId}, {TenantScope.Value}, {profileKey}, {profileName}, {string.Empty},
                     {(int)Gma.Modules.AccessControl.Domain.Enums.AccessProfileStatus.Active}, {1L},
                     {(int)AccessProfileSubjectKind.System}, {actorId},
                     {Now}, {(int)AccessProfileSubjectKind.System}, {actorId}, {Now});

                INSERT INTO access.access_profile_assignments
                    ("Id", "ProfileId", "SubjectKind", "SubjectId", "CreatedByKind", "CreatedById", "CreatedAtUtc")
                VALUES
                    ({assignmentId}, {profileId}, {(int)AccessProfileSubjectKind.User}, {subjectId},
                     {(int)AccessProfileSubjectKind.System}, {actorId}, {Now});
                """);
            await legacy.Database.MigrateAsync();
        }

        await using AccessControlDbContext verification = CreateDbContext(connectionString);
        AccessProfileAssignment assignment = await verification.AccessProfileAssignments.SingleAsync();
        Assert.Equal(TenantScope.Value, assignment.AssignmentScopeValue);
    }

    [DockerFact]
    public async Task Concurrent_bootstrap_has_exactly_one_winner()
    {
        await using PostgreSqlContainer postgreSql = CreatePostgreSql("access_bootstrap_tests");
        await postgreSql.StartAsync();
        string connectionString = postgreSql.GetConnectionString();
        await MigrateAsync(connectionString);

        Task<bool> first = BootstrapAsync(connectionString, "owner-a");
        Task<bool> second = BootstrapAsync(connectionString, "owner-b");
        bool[] outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes, outcome => outcome);
        Assert.Single(outcomes, outcome => !outcome);
        await using AccessControlDbContext verification = CreateDbContext(connectionString);
        Assert.Single(await verification.SubjectRoleAssignments.ToArrayAsync());
        Assert.Single(await verification.RolePermissions.Where(permission =>
            permission.PermissionCode == AccessControlPermissionGrants.OwnerWildcard).ToArrayAsync());
    }

    [DockerFact]
    public async Task Concurrent_final_owner_removals_leave_one_protected_owner()
    {
        await using PostgreSqlContainer postgreSql = CreatePostgreSql("access_owner_guard_tests");
        await postgreSql.StartAsync();
        string connectionString = postgreSql.GetConnectionString();
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

        Task<AccessControlRemovalOutcome> first = RemoveOwnerAsync(connectionString, ownerA);
        Task<AccessControlRemovalOutcome> second = RemoveOwnerAsync(connectionString, ownerB);
        AccessControlRemovalOutcome[] outcomes = await Task.WhenAll(first, second);

        Assert.Contains(AccessControlRemovalOutcome.Removed, outcomes);
        Assert.Contains(AccessControlRemovalOutcome.LastOwnerProtected, outcomes);
        await using AccessControlDbContext verification = CreateDbContext(connectionString);
        Assert.Single(await verification.SubjectRoleAssignments.ToArrayAsync());
    }

    [DockerFact]
    public async Task Compatibility_role_definition_removes_stale_permissions()
    {
        await using PostgreSqlContainer postgreSql = CreatePostgreSql("access_role_reconciliation_tests");
        await postgreSql.StartAsync();
        string connectionString = postgreSql.GetConnectionString();
        await MigrateAsync(connectionString);

        await using (AccessControlDbContext writer = CreateDbContext(connectionString))
        {
            AccessControlRbacRepository repository = CreateRbacRepository(writer);
            await repository.EnsureRoleDefinitionAsync(
                "workspace-member",
                ["properties.read", "reservations.manage"],
                Now,
                CancellationToken.None);
            await repository.EnsureRoleDefinitionAsync(
                "workspace-member",
                ["properties.read"],
                Now.AddMinutes(1),
                CancellationToken.None);
        }

        await using AccessControlDbContext verification = CreateDbContext(connectionString);
        Assert.Equal(
            ["properties.read"],
            await verification.RolePermissions
                .OrderBy(permission => permission.PermissionCode)
                .Select(permission => permission.PermissionCode)
                .ToArrayAsync());
    }

    [DockerFact]
    public async Task Transactional_commands_are_serialized_by_the_management_lock()
    {
        await using PostgreSqlContainer postgreSql = CreatePostgreSql("access_command_lock_tests");
        await postgreSql.StartAsync();
        string connectionString = postgreSql.GetConnectionString();
        await MigrateAsync(connectionString);

        await AssertTransactionalCommandsSerializeAsync(connectionString);
        await AssertBootstrapUsesCurrentTransactionAsync(connectionString);
    }

    [DockerFact]
    public async Task Scoped_profile_authorization_is_one_query_and_archive_fails_closed()
    {
        await using PostgreSqlContainer postgreSql = CreatePostgreSql("access_profile_authorization_tests");
        await postgreSql.StartAsync();
        string connectionString = postgreSql.GetConnectionString();
        await MigrateAsync(connectionString);
        const string permissionCode = "reservations.read";
        const string secondPermissionCode = "guests.read";
        AccessSubject subject = AccessSubject.User("user-a");
        AccessSubject propertySubject = AccessSubject.User("property-user");
        AccessScope propertyScope = AccessScope.Parse("tenant:tenant-a/property:property-a");
        AccessScope siblingPropertyScope = AccessScope.Parse("tenant:tenant-a/property:property-b");
        AccessProfile profile = CreateProfile("front-desk", [permissionCode, secondPermissionCode]);
        AccessProfile propertyProfile = CreateProfile("property-front-desk", [permissionCode]);
        await using (AccessControlDbContext seed = CreateDbContext(connectionString))
        {
            AccessControlRbacRepository rbac = CreateRbacRepository(seed);
            await rbac.EnsureSubjectAsync(subject, Now, CancellationToken.None);
            await rbac.EnsureSubjectAsync(propertySubject, Now, CancellationToken.None);
            seed.AccessProfiles.AddRange(profile, propertyProfile);
            seed.AccessProfileAssignments.AddRange(
                CreateAssignment(profile.Id, subject),
                CreateAssignment(propertyProfile.Id, propertySubject, propertyScope));
            profile.RecordAssignmentChange(
                Guid.NewGuid(), DomainChangeKind.Assigned, Actor, ToDomain(subject), Now.AddMinutes(1));
            await seed.SaveChangesAsync();
        }

        CountingCommandInterceptor commands = new();
        await using (AccessControlDbContext reader = CreateDbContext(connectionString, commands))
        {
            AccessControlRbacRepository rbac = CreateRbacRepository(reader);
            IReadOnlyList<bool> batch = await rbac.HasPermissionsAsync(
            [
                new AccessRequirement(subject, PermissionCode.Create(permissionCode), TenantScope),
                new AccessRequirement(subject, PermissionCode.Create(secondPermissionCode), TenantScope),
                new AccessRequirement(subject, PermissionCode.Create("inventory.read"), TenantScope)
            ], CancellationToken.None);

            Assert.Equal([true, true, false], batch);
            Assert.Equal(1, commands.ReaderCommands);
            bool otherTenantDenied = await rbac.HasPermissionAsync(
                subject,
                PermissionCode.Create(permissionCode),
                AccessScope.Parse("tenant:tenant-b"),
                CancellationToken.None);
            Assert.False(otherTenantDenied);
            Assert.Equal(2, commands.ReaderCommands);
            Assert.Empty(reader.ChangeTracker.Entries());
        }

        await using (AccessControlDbContext scopedReader = CreateDbContext(connectionString))
        {
            AccessControlRbacRepository rbac = CreateRbacRepository(scopedReader);
            Assert.True(await rbac.HasPermissionAsync(
                propertySubject, PermissionCode.Create(permissionCode), propertyScope, CancellationToken.None));
            Assert.False(await rbac.HasPermissionAsync(
                propertySubject, PermissionCode.Create(permissionCode), TenantScope, CancellationToken.None));
            Assert.False(await rbac.HasPermissionAsync(
                propertySubject, PermissionCode.Create(permissionCode), siblingPropertyScope, CancellationToken.None));
        }

        await using (AccessControlDbContext writer = CreateDbContext(connectionString))
        {
            AccessProfileRepository profiles = new(writer);
            AccessProfile stored = (await profiles.GetAsync(
                profile.Id, TenantScope, tracking: true, CancellationToken.None))!;
            Assert.True(stored.Archive(
                stored.Version, Actor, Guid.NewGuid(), Now.AddMinutes(2)).IsSuccess);
            await writer.SaveChangesAsync();
            AccessControlPage<AccessProfileChangeDetails> history = await profiles.ListChangesAsync(
                profile.Id, TenantScope, PageRequest.Normalize(1, 10), CancellationToken.None);
            Assert.Equal([ContractChangeKind.Archived, ContractChangeKind.Assigned, ContractChangeKind.Created],
                history.Items.Select(change => change.Kind));
        }

        await using AccessControlDbContext verification = CreateDbContext(connectionString);
        AccessControlRbacRepository verificationRbac = CreateRbacRepository(verification);
        Assert.False(await verificationRbac.HasPermissionAsync(
            subject, PermissionCode.Create(permissionCode), TenantScope, CancellationToken.None));
    }

    [DockerFact]
    public async Task Profile_provisioner_reads_subject_assignments_with_scope_isolation()
    {
        await using PostgreSqlContainer postgreSql = CreatePostgreSql("access_profile_assignment_read_tests");
        await postgreSql.StartAsync();
        string connectionString = postgreSql.GetConnectionString();
        await MigrateAsync(connectionString);
        AccessScope otherTenantScope = AccessScope.Parse("tenant:tenant-b");
        AccessSubject subject = AccessSubject.User("user-a");
        AccessProfile assignedProfile = CreateProfile(TenantScope, "front-desk", ["reservations.read"]);
        AccessProfile unassignedProfile = CreateProfile(TenantScope, "manager", ["staff.manage"]);
        AccessProfile propertyAssignedProfile = CreateProfile(TenantScope, "housekeeping", ["inventory.read"]);
        AccessScope propertyScope = AccessScope.Parse("tenant:tenant-a/property:property-a");
        AccessProfile otherTenantProfile = CreateProfile(otherTenantScope, "front-desk", ["reservations.read"]);
        await using (AccessControlDbContext seed = CreateDbContext(connectionString))
        {
            AccessControlRbacRepository rbac = CreateRbacRepository(seed);
            await rbac.EnsureSubjectAsync(subject, Now, CancellationToken.None);
            seed.AccessProfiles.AddRange(
                assignedProfile,
                unassignedProfile,
                propertyAssignedProfile,
                otherTenantProfile);
            seed.AccessProfileAssignments.AddRange(
                CreateAssignment(assignedProfile.Id, subject),
                CreateAssignment(propertyAssignedProfile.Id, subject, propertyScope),
                CreateAssignment(otherTenantProfile.Id, subject, otherTenantScope));
            await seed.SaveChangesAsync();
        }

        await using AccessControlDbContext reader = CreateDbContext(connectionString);
        AccessProfileProvisioner provisioner = new(
            dispatcher: null!,
            new AccessProfileRepository(reader));

        AccessProfileAssignmentSet tenantAssignments = await provisioner.GetSubjectAssignmentsAsync(
            subject, TenantScope, CancellationToken.None);
        AccessProfileAssignmentSet otherTenantAssignments = await provisioner.GetSubjectAssignmentsAsync(
            subject, otherTenantScope, CancellationToken.None);
        AccessProfileAssignmentSet noAssignments = await provisioner.GetSubjectAssignmentsAsync(
            AccessSubject.User("user-b"), TenantScope, CancellationToken.None);
        ScopedAccessProfileAssignmentSet scopedAssignments = await provisioner.GetSubjectScopedAssignmentsAsync(
            subject, TenantScope, CancellationToken.None);

        AccessProfileDto tenantProfile = Assert.Single(tenantAssignments.Profiles);
        Assert.Equal(assignedProfile.Id, tenantProfile.Id);
        Assert.Equal(TenantScope.Value, tenantProfile.OwnerScope);
        Assert.Equal(1, tenantProfile.AssignmentCount);
        Assert.Equal(otherTenantProfile.Id, Assert.Single(otherTenantAssignments.Profiles).Id);
        Assert.Empty(noAssignments.Profiles);
        Assert.Collection(
            scopedAssignments.Assignments.OrderBy(assignment => assignment.AssignmentScope.Value),
            assignment =>
            {
                Assert.Equal(assignedProfile.Id, assignment.Profile.Id);
                Assert.Equal(TenantScope, assignment.AssignmentScope);
            },
            assignment =>
            {
                Assert.Equal(propertyAssignedProfile.Id, assignment.Profile.Id);
                Assert.Equal(propertyScope, assignment.AssignmentScope);
            });
        Assert.Empty(reader.ChangeTracker.Entries());
    }

    [DockerFact]
    public async Task Concurrent_profile_updates_have_one_optimistic_concurrency_winner()
    {
        await using PostgreSqlContainer postgreSql = CreatePostgreSql("access_profile_version_tests");
        await postgreSql.StartAsync();
        string connectionString = postgreSql.GetConnectionString();
        await MigrateAsync(connectionString);
        AccessProfile profile = CreateProfile("front-desk", ["reservations.read"]);
        await using (AccessControlDbContext seed = CreateDbContext(connectionString))
        {
            seed.AccessProfiles.Add(profile);
            await seed.SaveChangesAsync();
        }

        await using AccessControlDbContext first = CreateDbContext(connectionString);
        await using AccessControlDbContext second = CreateDbContext(connectionString);
        AccessProfile firstProfile = await first.AccessProfiles.Include(candidate => candidate.Permissions).SingleAsync();
        AccessProfile secondProfile = await second.AccessProfiles.Include(candidate => candidate.Permissions).SingleAsync();
        Assert.True(firstProfile.Update(
            "Front desk A", null, ["reservations.read"], 1,
            Actor, Guid.NewGuid(), Now.AddMinutes(1)).IsSuccess);
        Assert.True(secondProfile.Update(
            "Front desk B", null, ["reservations.read"], 1,
            Actor, Guid.NewGuid(), Now.AddMinutes(1)).IsSuccess);

        Exception?[] failures = await Task.WhenAll(
            CaptureAsync(() => first.SaveChangesAsync()),
            CaptureAsync(() => second.SaveChangesAsync()));

        Assert.Single(failures, failure => failure is null);
        Assert.Single(failures, failure => failure is DbUpdateConcurrencyException);
        await using AccessControlDbContext verification = CreateDbContext(connectionString);
        Assert.Equal(2, (await verification.AccessProfiles.SingleAsync()).Version);
        Assert.Equal(2, await verification.AccessProfileChanges.CountAsync());
    }

    [DockerFact]
    public async Task Concurrent_duplicate_profile_keys_have_one_unique_constraint_winner()
    {
        await using PostgreSqlContainer postgreSql = CreatePostgreSql("access_profile_key_tests");
        await postgreSql.StartAsync();
        string connectionString = postgreSql.GetConnectionString();
        await MigrateAsync(connectionString);
        await using AccessControlDbContext first = CreateDbContext(connectionString);
        await using AccessControlDbContext second = CreateDbContext(connectionString);
        first.AccessProfiles.Add(CreateProfile("front-desk", []));
        second.AccessProfiles.Add(CreateProfile("front-desk", []));

        Exception?[] failures = await Task.WhenAll(
            CaptureAsync(() => first.SaveChangesAsync()),
            CaptureAsync(() => second.SaveChangesAsync()));

        Assert.Single(failures, failure => failure is null);
        Assert.Single(failures, failure => failure is DbUpdateException);
        await using AccessControlDbContext verification = CreateDbContext(connectionString);
        Assert.Single(await verification.AccessProfiles.ToArrayAsync());
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
            owner, "owner", AccessScope.Global, CancellationToken.None);
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

    private static AccessProfile CreateProfile(string key, IReadOnlyCollection<string> permissions)
        => CreateProfile(TenantScope, key, permissions);

    private static AccessProfile CreateProfile(
        AccessScope ownerScope,
        string key,
        IReadOnlyCollection<string> permissions)
    {
        Result<AccessProfile> result = AccessProfile.Create(
            Guid.NewGuid(), ownerScope.Value, key, key, null,
            permissions, Actor, Guid.NewGuid(), Now);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static AccessProfileAssignment CreateAssignment(
        Guid profileId,
        AccessSubject subject,
        AccessScope? assignmentScope = null)
    {
        Result<AccessProfileAssignment> result = AccessProfileAssignment.Create(
            Guid.NewGuid(), profileId, (assignmentScope ?? TenantScope).Value, ToDomain(subject), Actor, Now);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static AccessProfileSubject ToDomain(AccessSubject subject) =>
        new(
            subject.Kind switch
            {
                AccessSubjectKind.User => AccessProfileSubjectKind.User,
                AccessSubjectKind.AdminActor => AccessProfileSubjectKind.AdminActor,
                AccessSubjectKind.Service => AccessProfileSubjectKind.Service,
                AccessSubjectKind.System => AccessProfileSubjectKind.System,
                _ => throw new ArgumentOutOfRangeException(nameof(subject))
            },
            subject.Id);

    private static AccessControlDbContext CreateDbContext(
        string connectionString,
        IInterceptor? interceptor = null)
    {
        DbContextOptionsBuilder<AccessControlDbContext> builder =
            new DbContextOptionsBuilder<AccessControlDbContext>()
                .UseNpgsql(connectionString, provider => provider
                    .MigrationsAssembly(AccessControlMigrations.PostgreSqlAssembly)
                    .MigrationsHistoryTable(
                        AccessControlMigrations.HistoryTable,
                        AccessControlMigrations.Schema));
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new AccessControlDbContext(builder.Options);
    }

    private static AccessControlRbacRepository CreateRbacRepository(AccessControlDbContext dbContext) =>
        new(dbContext, new RandomIdGenerator(), new DefaultScopeMatchOptionsResolver());

    private static PostgreSqlContainer CreatePostgreSql(string database) =>
        new PostgreSqlBuilder("postgres:16-alpine").WithDatabase(database).Build();

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

    private sealed class DefaultScopeMatchOptionsResolver : IAccessScopeMatchOptionsResolver
    {
        public AccessScopeMatchOptions Resolve(PermissionCode permission) => new();
    }

    private sealed class CountingCommandInterceptor : DbCommandInterceptor
    {
        public int ReaderCommands { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            this.ReaderCommands++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
