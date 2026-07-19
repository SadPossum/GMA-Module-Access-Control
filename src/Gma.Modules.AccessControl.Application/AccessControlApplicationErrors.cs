namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.Results;
using Gma.Modules.AccessControl.Domain.Errors;

public static class AccessControlApplicationErrors
{
    public static readonly Error RoleAlreadyExists = new("AccessControl.RoleAlreadyExists", "An access-control role with this name already exists.");
    public static readonly Error RoleNotFound = new("AccessControl.RoleNotFound", "The requested access-control role was not found.");
    public static readonly Error RoleNameInvalid = new("AccessControl.RoleNameInvalid", "The access-control role name is not valid.");
    public static readonly Error PermissionCodeInvalid = new("AccessControl.PermissionCodeInvalid", "The permission code is not valid.");
    public static readonly Error PermissionAlreadyGranted = new("AccessControl.PermissionAlreadyGranted", "The role already has this permission.");
    public static readonly Error PermissionNotGranted = new("AccessControl.PermissionNotGranted", "The role does not have this permission.");
    public static readonly Error AssignmentAlreadyExists = new("AccessControl.AssignmentAlreadyExists", "The subject already has this role assignment.");
    public static readonly Error AssignmentNotFound = new("AccessControl.AssignmentNotFound", "The requested role assignment was not found.");
    public static readonly Error SubjectRequired = new("AccessControl.SubjectRequired", "An access subject id is required.");
    public static readonly Error SubjectInvalid = new("AccessControl.SubjectInvalid", "The access subject is not valid.");
    public static readonly Error RoleNameRequired = new("AccessControl.RoleNameRequired", "An access-control role name is required.");
    public static readonly Error ScopeInvalid = new("AccessControl.ScopeInvalid", "The access-control scope is not valid.");
    public static readonly Error BootstrapNotAllowed = new("AccessControl.BootstrapNotAllowed", "Access-control bootstrap is not allowed because assignments already exist.");
    public static readonly Error LastOwnerProtected = new("AccessControl.LastOwnerProtected", "The final global owner permission or assignment cannot be removed.");
    public static readonly Error ProfileNotFound = new("AccessControl.ProfileNotFound", "The requested access profile was not found in the owning scope.");
    public static readonly Error ProfileAlreadyExists = new("AccessControl.ProfileAlreadyExists", "An access profile with this key already exists in the owning scope.");
    public static readonly Error ProfileAssignmentAlreadyExists = new("AccessControl.ProfileAssignmentAlreadyExists", "The subject already has this access profile.");
    public static readonly Error ProfileAssignmentNotFound = new("AccessControl.ProfileAssignmentNotFound", "The requested access-profile assignment was not found.");
    public static readonly Error ProfilePermissionNotAllowed = new("AccessControl.ProfilePermissionNotAllowed", "The permission is not eligible for scoped access profiles.");
    public static readonly Error ProfilePermissionEscalation = new("AccessControl.ProfilePermissionEscalation", "The actor cannot delegate a permission they do not hold in the owning scope.");
    public static Error ProfileScopeRequired => AccessProfileDomainErrors.ScopeRequired;
    public static Error ProfileKeyInvalid => AccessProfileDomainErrors.KeyInvalid;
    public static Error ProfileDisplayNameInvalid => AccessProfileDomainErrors.DisplayNameInvalid;
    public static Error ProfileDescriptionInvalid => AccessProfileDomainErrors.DescriptionInvalid;
    public static Error ProfilePermissionInvalid => AccessProfileDomainErrors.PermissionInvalid;
    public static Error ProfilePermissionLimitExceeded => AccessProfileDomainErrors.PermissionLimitExceeded;
    public static Error ProfileVersionConflict => AccessProfileDomainErrors.VersionConflict;
    public static Error ProfileArchived => AccessProfileDomainErrors.Archived;
    public static Error ProfileAlreadyArchived => AccessProfileDomainErrors.AlreadyArchived;
}
