namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.Domain.ValueObjects;

internal static class AccessProfileSubjectMappings
{
    public static AccessProfileSubject ToDomain(AccessSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        AccessProfileSubjectKind kind = subject.Kind switch
        {
            AccessSubjectKind.User => AccessProfileSubjectKind.User,
            AccessSubjectKind.AdminActor => AccessProfileSubjectKind.AdminActor,
            AccessSubjectKind.Service => AccessProfileSubjectKind.Service,
            AccessSubjectKind.System => AccessProfileSubjectKind.System,
            _ => throw new ArgumentOutOfRangeException(nameof(subject), subject.Kind, "The access subject kind is invalid.")
        };
        return new AccessProfileSubject(kind, subject.Id);
    }
}
