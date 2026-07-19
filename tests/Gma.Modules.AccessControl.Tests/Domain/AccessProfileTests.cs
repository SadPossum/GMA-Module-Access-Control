namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.Results;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.Domain.Errors;
using Gma.Modules.AccessControl.Domain.ValueObjects;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessProfileTests
{
    private const string OwnerScope = "tenant:tenant-a";
    private static readonly AccessProfileSubject Actor = new(AccessProfileSubjectKind.AdminActor, "actor-a");
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_requires_a_non_global_owner_scope()
    {
        Result<AccessProfile> result = AccessProfile.Create(
            Guid.NewGuid(),
            AccessProfileOwnerScope.GlobalValue,
            "front-desk",
            "Front desk",
            null,
            ["reservations.read"],
            Actor,
            Guid.NewGuid(),
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal(AccessProfileDomainErrors.ScopeRequired, result.Error);
    }

    [Fact]
    public void Create_normalizes_identity_and_deduplicates_permissions()
    {
        AccessProfile profile = CreateProfile(
            key: "  FRONT-DESK  ",
            permissions: ["staff.manage", "reservations.read", "reservations.read"]);

        Assert.Equal("front-desk", profile.Key);
        Assert.Equal(AccessProfileStatus.Active, profile.Status);
        Assert.Equal(1, profile.Version);
        Assert.Equal(["reservations.read", "staff.manage"],
            profile.Permissions.Select(permission => permission.PermissionCode));
        Assert.Equal(AccessProfileChangeKind.Created, Assert.Single(profile.Changes).Kind);
    }

    [Fact]
    public void Update_and_archive_enforce_optimistic_version_and_immutable_history()
    {
        AccessProfile profile = CreateProfile(permissions: ["reservations.read"]);

        Result updated = profile.Update(
            "Front desk lead",
            "Coordinates arrivals.",
            ["reservations.manage"],
            expectedVersion: 1,
            Actor,
            Guid.NewGuid(),
            Now.AddMinutes(1));
        Result staleUpdate = profile.Update(
            "Stale update",
            null,
            [],
            expectedVersion: 1,
            Actor,
            Guid.NewGuid(),
            Now.AddMinutes(2));
        Result archived = profile.Archive(
            expectedVersion: 2,
            Actor,
            Guid.NewGuid(),
            Now.AddMinutes(3));
        Result updateAfterArchive = profile.Update(
            "Archived update",
            null,
            [],
            expectedVersion: 3,
            Actor,
            Guid.NewGuid(),
            Now.AddMinutes(4));

        Assert.True(updated.IsSuccess);
        Assert.Equal(AccessProfileDomainErrors.VersionConflict, staleUpdate.Error);
        Assert.True(archived.IsSuccess);
        Assert.Equal(AccessProfileDomainErrors.Archived, updateAfterArchive.Error);
        Assert.Equal(AccessProfileStatus.Archived, profile.Status);
        Assert.Equal(3, profile.Version);
        Assert.Equal("Front desk lead", profile.DisplayName);
        Assert.Equal(["reservations.manage"],
            profile.Permissions.Select(permission => permission.PermissionCode));
        Assert.Equal(
            [AccessProfileChangeKind.Created, AccessProfileChangeKind.Updated, AccessProfileChangeKind.Archived],
            profile.Changes.Select(change => change.Kind));
        Assert.Equal([1L, 2L, 3L], profile.Changes.Select(change => change.ProfileVersion));
    }

    [Fact]
    public void Create_rejects_more_than_the_bounded_permission_limit()
    {
        string[] permissions = Enumerable.Range(1, AccessProfile.MaxPermissionCount + 1)
            .Select(index => $"module.permission-{index}")
            .ToArray();

        Result<AccessProfile> result = AccessProfile.Create(
            Guid.NewGuid(), OwnerScope, "front-desk", "Front desk", null,
            permissions, Actor, Guid.NewGuid(), Now);

        Assert.True(result.IsFailure);
        Assert.Equal(AccessProfileDomainErrors.PermissionLimitExceeded, result.Error);
    }

    [Fact]
    public void Assignment_changes_record_actor_and_subject_without_rewriting_profile_version()
    {
        AccessProfile profile = CreateProfile();
        AccessProfileSubject subject = new(AccessProfileSubjectKind.User, "user-a");

        profile.RecordAssignmentChange(
            Guid.NewGuid(), AccessProfileChangeKind.Assigned, Actor, subject, Now.AddMinutes(1));
        profile.RecordAssignmentChange(
            Guid.NewGuid(), AccessProfileChangeKind.Unassigned, Actor, subject, Now.AddMinutes(2));

        Assert.Equal(1, profile.Version);
        Assert.Equal(
            [AccessProfileChangeKind.Created, AccessProfileChangeKind.Assigned, AccessProfileChangeKind.Unassigned],
            profile.Changes.Select(change => change.Kind));
        Assert.All(profile.Changes.Skip(1), change =>
        {
            Assert.Equal(Actor.Id, change.ActorId);
            Assert.Equal(subject.Id, change.SubjectId);
            Assert.Equal(1, change.ProfileVersion);
        });
    }

    private static AccessProfile CreateProfile(
        string key = "front-desk",
        IReadOnlyCollection<string>? permissions = null)
    {
        Result<AccessProfile> result = AccessProfile.Create(
            Guid.NewGuid(), OwnerScope, key, "Front desk", null,
            permissions ?? [], Actor, Guid.NewGuid(), Now);
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
