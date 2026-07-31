namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record AccessProfileMutationAdmissionContext(
    AccessProfileMutationAdmissionOperation Operation,
    AccessScope OwnerScope,
    AccessSubject Actor,
    Guid? ProfileId = null,
    string? ProfileKey = null);
