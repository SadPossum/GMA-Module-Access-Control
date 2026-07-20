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
    public async Task Scoped_profile_authorization_is_one_query_and_archive_fails_closed()
    {
        await using PostgreSqlContainer postgreSql = CreatePostgreSql("access_profile_authorization_tests");
        await postgreSql.StartAsync();
        string connectionString = postgreSql.GetConnectionString();
        await MigrateAsync(connectionString);
        const string permissionCode = "reservations.read";
        const string secondPermissionCode = "guests.read";
        AccessSubject subject = AccessSubject.User("user-a");
        AccessProfile profile = CreateProfile("front-desk", [permissionCode, secondPermissionCode]);
        await using (AccessControlDbContext seed = CreateDbContext(connectionString))
        {
            AccessControlRbacRepository rbac = CreateRbacRepository(seed);
            await rbac.EnsureSubjectAsync(subject, Now, CancellationToken.None);
            seed.AccessProfiles.Add(profile);
            seed.AccessProfileAssignments.Add(CreateAssignment(profile.Id, subject));
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
            Guid.NewGuid(), profileId, ToDomain(subject), Actor, Now);
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
