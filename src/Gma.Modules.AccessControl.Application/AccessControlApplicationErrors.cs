namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.Results;

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
}
