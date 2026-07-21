namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Framework.Results;
using Gma.Framework.Runtime.Identity;
using Gma.Framework.Runtime.Time;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Handlers;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.Domain.ValueObjects;
using Gma.Modules.AccessControl.Persistence;
using Gma.Modules.AccessControl.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessProfileAssignmentRevokerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly AccessScope ScopeA = AccessScope.Parse("tenant:tenant-a");
    private static readonly AccessScope PropertyA =
        AccessScope.Parse("tenant:tenant-a/property:property-a");
    private static readonly AccessScope ScopeB = AccessScope.Parse("tenant:tenant-b");
    private static readonly AccessSubject Target = AccessSubject.User("target-a");
    private static readonly AccessSubject Other = AccessSubject.User("other-a");
    private static readonly AccessSubject Actor = AccessSubject.System("membership-sync");

    [Fact]
    public async Task Revoke_all_removes_exact_subject_scope_assignments_and_records_history()
    {
        DbContextOptions<AccessControlDbContext> options = new DbContextOptionsBuilder<AccessControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using AccessControlDbContext dbContext = new(options);
        AccessProfile active = CreateProfile(ScopeA, "active");
        AccessProfile archived = CreateProfile(ScopeA, "archived");
        Assert.True(archived.Archive(archived.Version, ToDomain(Actor), Guid.NewGuid(), Now).IsSuccess);
        AccessProfile otherScope = CreateProfile(ScopeB, "other-scope");
        dbContext.AccessProfiles.AddRange(active, archived, otherScope);
        dbContext.AccessProfileAssignments.AddRange(
            CreateAssignment(active.Id, ScopeA, Target),
            CreateAssignment(active.Id, PropertyA, Target),
            CreateAssignment(archived.Id, ScopeA, Target),
            CreateAssignment(otherScope.Id, ScopeB, Target),
            CreateAssignment(active.Id, ScopeA, Other));
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
        AccessProfileRepository repository = new(dbContext);
        RevokeAccessProfileAssignmentsCommandHandler handler = new(
            repository,
            new SequentialIdGenerator(),
            new FixedClock(Now.AddMinutes(1)));

        Result<int> result = await handler.HandleAsync(
            new RevokeAccessProfileAssignmentsCommand(Target, ScopeA, Actor),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value);
        Assert.Equal(2, await dbContext.AccessProfileAssignments.CountAsync());
        Assert.Single(await dbContext.AccessProfileAssignments.Where(assignment =>
            assignment.SubjectId == Target.Id).ToArrayAsync());
        Assert.Equal(3, await dbContext.AccessProfileChanges.CountAsync(change =>
            change.Kind == AccessProfileChangeKind.Unassigned &&
            change.ActorId == Actor.Id &&
            change.SubjectId == Target.Id));

        dbContext.ChangeTracker.Clear();
        Result<int> replay = await handler.HandleAsync(
            new RevokeAccessProfileAssignmentsCommand(Target, ScopeA, Actor),
            CancellationToken.None);
        Assert.True(replay.IsSuccess);
        Assert.Equal(0, replay.Value);
    }

    private static AccessProfile CreateProfile(AccessScope scope, string key)
    {
        Result<AccessProfile> result = AccessProfile.Create(
            Guid.NewGuid(), scope.Value, key, key, null, ["reservations.read"],
            ToDomain(Actor), Guid.NewGuid(), Now);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static AccessProfileAssignment CreateAssignment(
        Guid profileId,
        AccessScope assignmentScope,
        AccessSubject subject)
    {
        Result<AccessProfileAssignment> result = AccessProfileAssignment.Create(
            Guid.NewGuid(), profileId, assignmentScope.Value, ToDomain(subject), ToDomain(Actor), Now);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static AccessProfileSubject ToDomain(AccessSubject subject) =>
        new((AccessProfileSubjectKind)subject.Kind, subject.Id);

    private sealed class SequentialIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
