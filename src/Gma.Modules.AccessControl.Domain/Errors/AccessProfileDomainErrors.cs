namespace Gma.Modules.AccessControl.Domain.Errors;

using Gma.Framework.Results;

public static class AccessProfileDomainErrors
{
    public static readonly Error IdRequired = new("AccessControl.ProfileIdRequired", "An access-profile id is required.");
    public static readonly Error ScopeRequired = new("AccessControl.ProfileScopeRequired", "A non-global owning scope is required.");
    public static readonly Error KeyInvalid = new("AccessControl.ProfileKeyInvalid", "The access-profile key is invalid.");
    public static readonly Error DisplayNameInvalid = new("AccessControl.ProfileDisplayNameInvalid", "The access-profile display name is invalid.");
    public static readonly Error DescriptionInvalid = new("AccessControl.ProfileDescriptionInvalid", "The access-profile description is invalid.");
    public static readonly Error ActorInvalid = new("AccessControl.ProfileActorInvalid", "The access-profile actor is invalid.");
    public static readonly Error EventIdRequired = new("AccessControl.ProfileEventIdRequired", "An access-profile change id is required.");
    public static readonly Error PermissionInvalid = new("AccessControl.ProfilePermissionInvalid", "An access-profile permission is invalid.");
    public static readonly Error PermissionLimitExceeded = new("AccessControl.ProfilePermissionLimitExceeded", "An access profile has too many permissions.");
    public static readonly Error VersionConflict = new("AccessControl.ProfileVersionConflict", "The access profile changed after it was loaded.");
    public static readonly Error Archived = new("AccessControl.ProfileArchived", "The access profile is archived.");
    public static readonly Error AlreadyArchived = new("AccessControl.ProfileAlreadyArchived", "The access profile is already archived.");
    public static readonly Error AssignmentIdRequired = new("AccessControl.ProfileAssignmentIdRequired", "An access-profile assignment id is required.");
}
