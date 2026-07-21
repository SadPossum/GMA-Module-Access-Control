namespace Gma.Modules.AccessControl.Domain.Aggregates;

using Gma.Framework.Domain.Models;
using Gma.Framework.Permissions;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Domain.Entities;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.Domain.Errors;
using Gma.Modules.AccessControl.Domain.ValueObjects;

public sealed class AccessProfile : AggregateRoot<Guid>
{
    public const int MaxPermissionCount = 100;

    private readonly List<AccessProfilePermission> permissions = [];
    private readonly List<AccessProfileChange> changes = [];

    private AccessProfile() { }
    private AccessProfile(Guid id) : base(id) { }

    public string OwnerScopeValue { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public AccessProfileStatus Status { get; private set; }
    public long Version { get; private set; } = 1;
    public int CreatedByKind { get; private set; }
    public string CreatedById { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public int LastChangedByKind { get; private set; }
    public string LastChangedById { get; private set; } = string.Empty;
    public DateTimeOffset LastChangedAtUtc { get; private set; }
    public IReadOnlyCollection<AccessProfilePermission> Permissions => this.permissions;
    public IReadOnlyCollection<AccessProfileChange> Changes => this.changes;

    public static Result<AccessProfile> Create(
        Guid id,
        string? ownerScopeValue,
        string key,
        string displayName,
        string? description,
        IReadOnlyCollection<string>? permissions,
        AccessProfileSubject? actor,
        Guid eventId,
        DateTimeOffset nowUtc)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<AccessProfile>(AccessProfileDomainErrors.IdRequired);
        }

        if (actor is null)
        {
            return Result.Failure<AccessProfile>(AccessProfileDomainErrors.ActorInvalid);
        }

        if (eventId == Guid.Empty)
        {
            return Result.Failure<AccessProfile>(AccessProfileDomainErrors.EventIdRequired);
        }

        Result<AccessProfileOwnerScope> ownerScope = AccessProfileOwnerScope.Create(ownerScopeValue);
        Result<AccessProfileKey> profileKey = AccessProfileKey.Create(key);
        Result<AccessProfileDisplayName> profileName = AccessProfileDisplayName.Create(displayName);
        Result<AccessProfileDescription> profileDescription = AccessProfileDescription.Create(description);
        Result<PermissionCode[]> normalizedPermissions = NormalizePermissions(permissions);
        if (ownerScope.IsFailure) return Result.Failure<AccessProfile>(ownerScope.Error);
        if (profileKey.IsFailure) return Result.Failure<AccessProfile>(profileKey.Error);
        if (profileName.IsFailure) return Result.Failure<AccessProfile>(profileName.Error);
        if (profileDescription.IsFailure) return Result.Failure<AccessProfile>(profileDescription.Error);
        if (normalizedPermissions.IsFailure) return Result.Failure<AccessProfile>(normalizedPermissions.Error);

        AccessProfile profile = new(id)
        {
            OwnerScopeValue = ownerScope.Value.Value,
            Key = profileKey.Value.Value,
            DisplayName = profileName.Value.Value,
            Description = profileDescription.Value.Value,
            Status = AccessProfileStatus.Active,
            CreatedByKind = (int)actor.Kind,
            CreatedById = actor.Id,
            CreatedAtUtc = nowUtc,
            LastChangedByKind = (int)actor.Kind,
            LastChangedById = actor.Id,
            LastChangedAtUtc = nowUtc
        };
        profile.ReplacePermissions(normalizedPermissions.Value, nowUtc);
        profile.changes.Add(new AccessProfileChange(
            eventId, id, AccessProfileChangeKind.Created, actor, profile.Version, nowUtc));
        return Result.Success(profile);
    }

    public Result Update(
        string displayName,
        string? description,
        IReadOnlyCollection<string>? permissions,
        long expectedVersion,
        AccessProfileSubject? actor,
        Guid eventId,
        DateTimeOffset nowUtc)
    {
        Result mutable = this.EnsureMutable(expectedVersion, actor, eventId);
        if (mutable.IsFailure) return mutable;

        Result<AccessProfileDisplayName> profileName = AccessProfileDisplayName.Create(displayName);
        Result<AccessProfileDescription> profileDescription = AccessProfileDescription.Create(description);
        Result<PermissionCode[]> normalizedPermissions = NormalizePermissions(permissions);
        if (profileName.IsFailure) return Result.Failure(profileName.Error);
        if (profileDescription.IsFailure) return Result.Failure(profileDescription.Error);
        if (normalizedPermissions.IsFailure) return Result.Failure(normalizedPermissions.Error);

        this.DisplayName = profileName.Value.Value;
        this.Description = profileDescription.Value.Value;
        this.ReplacePermissions(normalizedPermissions.Value, nowUtc);
        this.Advance(actor!, nowUtc);
        this.changes.Add(new AccessProfileChange(
            eventId, this.Id, AccessProfileChangeKind.Updated, actor!, this.Version, nowUtc));
        return Result.Success();
    }

    public Result Archive(
        long expectedVersion,
        AccessProfileSubject? actor,
        Guid eventId,
        DateTimeOffset nowUtc)
    {
        if (this.Status == AccessProfileStatus.Archived)
        {
            return Result.Failure(AccessProfileDomainErrors.AlreadyArchived);
        }

        Result mutable = this.EnsureMutable(expectedVersion, actor, eventId);
        if (mutable.IsFailure) return mutable;

        this.Status = AccessProfileStatus.Archived;
        this.Advance(actor!, nowUtc);
        this.changes.Add(new AccessProfileChange(
            eventId, this.Id, AccessProfileChangeKind.Archived, actor!, this.Version, nowUtc));
        return Result.Success();
    }

    public AccessProfileChange RecordAssignmentChange(
        Guid eventId,
        AccessProfileChangeKind kind,
        AccessProfileSubject actor,
        AccessProfileSubject subject,
        DateTimeOffset nowUtc,
        string? assignmentScopeValue = null)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("An access-profile change id is required.", nameof(eventId));
        }

        if (kind is not (AccessProfileChangeKind.Assigned or AccessProfileChangeKind.Unassigned))
        {
            throw new ArgumentException("The change kind must describe an assignment mutation.", nameof(kind));
        }

        Result<AccessProfileAssignmentScope> assignmentScope =
            AccessProfileAssignmentScope.Create(assignmentScopeValue ?? this.OwnerScopeValue);
        if (assignmentScope.IsFailure)
        {
            throw new ArgumentException(assignmentScope.Error.Message, nameof(assignmentScopeValue));
        }

        AccessProfileChange change = new(
            eventId, this.Id, kind, actor, this.Version, nowUtc, subject, assignmentScope.Value);
        this.changes.Add(change);
        return change;
    }

    private Result EnsureMutable(long expectedVersion, AccessProfileSubject? actor, Guid eventId)
    {
        if (this.Status == AccessProfileStatus.Archived)
        {
            return Result.Failure(AccessProfileDomainErrors.Archived);
        }

        if (expectedVersion != this.Version)
        {
            return Result.Failure(AccessProfileDomainErrors.VersionConflict);
        }

        if (actor is null)
        {
            return Result.Failure(AccessProfileDomainErrors.ActorInvalid);
        }

        return eventId == Guid.Empty
            ? Result.Failure(AccessProfileDomainErrors.EventIdRequired)
            : Result.Success();
    }

    private void Advance(AccessProfileSubject actor, DateTimeOffset nowUtc)
    {
        this.Version++;
        this.LastChangedByKind = (int)actor.Kind;
        this.LastChangedById = actor.Id;
        this.LastChangedAtUtc = nowUtc;
    }

    private void ReplacePermissions(IEnumerable<PermissionCode> permissionCodes, DateTimeOffset nowUtc)
    {
        string[] desired = permissionCodes.Select(permission => permission.Value).ToArray();
        this.permissions.RemoveAll(permission => !desired.Contains(permission.PermissionCode, StringComparer.Ordinal));
        foreach (string permissionCode in desired)
        {
            if (!this.permissions.Any(permission => permission.PermissionCode == permissionCode))
            {
                this.permissions.Add(new AccessProfilePermission(
                    this.Id, PermissionCode.Create(permissionCode), nowUtc));
            }
        }
    }

    private static Result<PermissionCode[]> NormalizePermissions(IReadOnlyCollection<string>? permissions)
    {
        if (permissions is null)
        {
            return Result.Failure<PermissionCode[]>(AccessProfileDomainErrors.PermissionInvalid);
        }

        if (permissions.Count > MaxPermissionCount)
        {
            return Result.Failure<PermissionCode[]>(AccessProfileDomainErrors.PermissionLimitExceeded);
        }

        List<PermissionCode> normalized = [];
        foreach (string permission in permissions)
        {
            if (!PermissionCode.TryCreate(permission, out PermissionCode? code))
            {
                return Result.Failure<PermissionCode[]>(AccessProfileDomainErrors.PermissionInvalid);
            }

            if (!normalized.Contains(code))
            {
                normalized.Add(code);
            }
        }

        return Result.Success(normalized.OrderBy(code => code.Value, StringComparer.Ordinal).ToArray());
    }
}
