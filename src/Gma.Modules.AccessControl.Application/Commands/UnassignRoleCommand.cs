namespace Gma.Modules.AccessControl.Application.Commands;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;

public sealed record UnassignRoleCommand(
    AccessSubjectKind SubjectKind,
    string SubjectId,
    string RoleName,
    AccessScope? AccessScope) : ITransactionalCommand<Unit>;
