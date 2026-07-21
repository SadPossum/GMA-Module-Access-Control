namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Application.Ports;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.ValueObjects;

internal sealed class AccessProfileProvisioner(
    IRequestDispatcher dispatcher,
    IAccessProfileRepository profiles) : IAccessProfileProvisioner
{
    public async Task<AccessProfileDto> EnsureProfileAsync(
        AccessScope ownerScope,
        AccessProfileDefinition definition,
        AccessSubject actor,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(ownerScope);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(actor);

        Result<AccessProfileDetails> result = await dispatcher.SendAsync(
                new EnsureAccessProfileCommand(ownerScope, definition, actor),
                cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? ToContract(result.Value)
            : throw new InvalidOperationException(result.Error.Message);
    }

    public async Task<AccessProfileDto?> FindProfileByKeyAsync(
        AccessScope ownerScope,
        string key,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(ownerScope);
        Result<AccessProfileKey> normalizedKey = AccessProfileKey.Create(key);
        if (normalizedKey.IsFailure)
        {
            throw new ArgumentException(normalizedKey.Error.Message, nameof(key));
        }

        AccessProfileDetails? profile = await profiles
            .GetDetailsByKeyAsync(ownerScope, normalizedKey.Value.Value, cancellationToken)
            .ConfigureAwait(false);
        return profile is null ? null : ToContract(profile);
    }

    public async Task<AccessProfileAssignmentSet> GetSubjectAssignmentsAsync(
        AccessSubject subject,
        AccessScope ownerScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ValidateScope(ownerScope);
        IReadOnlyList<AccessProfileDetails> assignedProfiles = await profiles
            .ListDetailsForSubjectAsync(subject, ownerScope, cancellationToken)
            .ConfigureAwait(false);
        return new AccessProfileAssignmentSet(
            subject,
            ownerScope,
            assignedProfiles.Select(ToContract).ToArray());
    }

    public async Task<AccessProfileAssignmentReconciliation> ReconcileSubjectAssignmentsAsync(
        AccessSubject subject,
        AccessScope ownerScope,
        IReadOnlyCollection<Guid> profileIds,
        AccessSubject actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ValidateScope(ownerScope);
        ArgumentNullException.ThrowIfNull(profileIds);
        ArgumentNullException.ThrowIfNull(actor);

        Result<AccessProfileAssignmentReconciliationDetails> result = await dispatcher.SendAsync(
                new ReconcileAccessProfileAssignmentsCommand(subject, ownerScope, profileIds, actor),
                cancellationToken)
            .ConfigureAwait(false);
        if (result.IsFailure)
        {
            throw new InvalidOperationException(result.Error.Message);
        }

        return new AccessProfileAssignmentReconciliation(
            result.Value.Subject,
            result.Value.OwnerScope,
            result.Value.ProfileIds,
            result.Value.AssignedCount,
            result.Value.UnassignedCount);
    }

    private static void ValidateScope(AccessScope ownerScope)
    {
        ArgumentNullException.ThrowIfNull(ownerScope);
        if (ownerScope.IsGlobal)
        {
            throw new ArgumentException("A non-global access-profile owner scope is required.", nameof(ownerScope));
        }
    }

    private static AccessProfileDto ToContract(AccessProfileDetails profile) =>
        new(
            profile.Id,
            profile.OwnerScope.Value,
            profile.Key,
            profile.DisplayName,
            profile.Description,
            profile.Status,
            profile.Version,
            profile.Permissions,
            profile.AssignmentCount,
            profile.CreatedAtUtc,
            profile.LastChangedAtUtc);
}
