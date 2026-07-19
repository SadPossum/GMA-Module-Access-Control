namespace Gma.Modules.AccessControl.Application.Validation;

using Gma.Framework.AccessControl;
using Gma.Framework.Permissions;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Errors;
using Gma.Modules.AccessControl.Domain.ValueObjects;

internal static class AccessProfileCommandValidation
{
    public static IEnumerable<string> ValidateProfileId(Guid profileId)
    {
        if (profileId == Guid.Empty)
        {
            yield return AccessProfileDomainErrors.IdRequired.Message;
        }
    }

    public static IEnumerable<string> ValidateOwnerScope(AccessScope? ownerScope)
    {
        if (ownerScope is null || ownerScope.IsGlobal)
        {
            yield return AccessProfileDomainErrors.ScopeRequired.Message;
        }
    }

    public static IEnumerable<string> ValidateActor(AccessSubject? actor)
    {
        if (actor is null)
        {
            yield return AccessProfileDomainErrors.ActorInvalid.Message;
        }
    }

    public static IEnumerable<string> ValidateSubject(AccessSubject? subject)
    {
        if (subject is null)
        {
            yield return AccessControlApplicationErrors.SubjectInvalid.Message;
        }
    }

    public static IEnumerable<string> ValidateExpectedVersion(long expectedVersion)
    {
        if (expectedVersion < 1)
        {
            yield return "The expected access-profile version must be positive.";
        }
    }

    public static IEnumerable<string> ValidateDefinition(
        string? key,
        string? displayName,
        string? description,
        IReadOnlyCollection<string>? permissions,
        bool validateKey)
    {
        if (validateKey && AccessProfileKey.Create(key).IsFailure)
        {
            yield return AccessProfileDomainErrors.KeyInvalid.Message;
        }

        if (AccessProfileDisplayName.Create(displayName).IsFailure)
        {
            yield return AccessProfileDomainErrors.DisplayNameInvalid.Message;
        }

        if (AccessProfileDescription.Create(description).IsFailure)
        {
            yield return AccessProfileDomainErrors.DescriptionInvalid.Message;
        }

        if (permissions is null || permissions.Any(permission => !PermissionCode.TryCreate(permission, out _)))
        {
            yield return AccessProfileDomainErrors.PermissionInvalid.Message;
        }
        else if (permissions.Count > AccessProfile.MaxPermissionCount)
        {
            yield return AccessProfileDomainErrors.PermissionLimitExceeded.Message;
        }
    }
}
