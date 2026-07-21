namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Permissions;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Contracts;

internal sealed class ScopedAccessProfileManager(
    IScopedAccessProfileProvisioner assignments,
    IRequestDispatcher dispatcher,
    IAccessAuthorizationService authorization) : IScopedAccessProfileManager
{
    public async Task<Result<ScopedAccessProfileAssignmentSet>> GetSubjectAssignmentsAsync(
        AccessSubject subject,
        AccessScope ownerScope,
        AccessSubject actor,
        CancellationToken cancellationToken = default)
    {
        Validate(subject, ownerScope, actor);
        Result access = await this.AuthorizeAsync(
            actor,
            ownerScope,
            AccessControlProfilePermissionCodes.Read,
            cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
        {
            return Result.Failure<ScopedAccessProfileAssignmentSet>(access.Error);
        }

        return Result.Success(await assignments.GetSubjectScopedAssignmentsAsync(
            subject,
            ownerScope,
            cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<ScopedAccessProfileAssignmentReconciliation>> ReconcileSubjectAssignmentsAsync(
        AccessSubject subject,
        AccessScope ownerScope,
        IReadOnlyCollection<AccessProfileAssignmentTarget> targets,
        AccessSubject actor,
        CancellationToken cancellationToken = default)
    {
        Validate(subject, ownerScope, actor);
        ArgumentNullException.ThrowIfNull(targets);
        Result access = await this.AuthorizeAsync(
            actor,
            ownerScope,
            AccessControlProfilePermissionCodes.Assign,
            cancellationToken).ConfigureAwait(false);
        if (access.IsFailure)
        {
            return Result.Failure<ScopedAccessProfileAssignmentReconciliation>(access.Error);
        }

        Result<ScopedAccessProfileAssignmentReconciliationDetails> result = await dispatcher.SendAsync(
            new ReconcileScopedAccessProfileAssignmentsCommand(subject, ownerScope, targets, actor),
            cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result.Failure<ScopedAccessProfileAssignmentReconciliation>(
                MapError(result.Error));
        }

        return Result.Success(new ScopedAccessProfileAssignmentReconciliation(
            result.Value.Subject,
            result.Value.OwnerScope,
            result.Value.Targets,
            result.Value.AssignedCount,
            result.Value.UnassignedCount));
    }

    private async Task<Result> AuthorizeAsync(
        AccessSubject actor,
        AccessScope ownerScope,
        string permissionCode,
        CancellationToken cancellationToken)
    {
        AccessDecision decision = await authorization.AuthorizeAsync(
            new AccessRequirement(actor, PermissionCode.Create(permissionCode), ownerScope),
            cancellationToken).ConfigureAwait(false);
        return decision.IsAllowed
            ? Result.Success()
            : Result.Failure(ScopedAccessProfileManagementErrors.AccessDenied);
    }

    private static Error MapError(Error error)
    {
        if (string.Equals(
            error.Code,
            AccessControlApplicationErrors.ProfileAssignmentRejected.Code,
            StringComparison.Ordinal))
        {
            return ScopedAccessProfileManagementErrors.AssignmentRejected;
        }

        if (string.Equals(
            error.Code,
            AccessControlApplicationErrors.ProfilePermissionEscalation.Code,
            StringComparison.Ordinal))
        {
            return ScopedAccessProfileManagementErrors.PermissionEscalation;
        }

        if (string.Equals(
            error.Code,
            AccessControlApplicationErrors.ProfileAssignmentScopeInvalid.Code,
            StringComparison.Ordinal))
        {
            return ScopedAccessProfileManagementErrors.ScopeInvalid;
        }

        return string.Equals(
            error.Code,
            AccessControlApplicationErrors.ProfileNotFound.Code,
            StringComparison.Ordinal)
            ? ScopedAccessProfileManagementErrors.ProfileUnavailable
            : error;
    }

    private static void Validate(
        AccessSubject subject,
        AccessScope ownerScope,
        AccessSubject actor)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(ownerScope);
        ArgumentNullException.ThrowIfNull(actor);
        if (ownerScope.IsGlobal)
        {
            throw new ArgumentException(
                "A non-global access-profile owner scope is required.",
                nameof(ownerScope));
        }
    }
}
