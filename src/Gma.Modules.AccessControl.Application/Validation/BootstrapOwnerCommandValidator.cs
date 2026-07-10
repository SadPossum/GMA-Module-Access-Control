namespace Gma.Modules.AccessControl.Application.Validation;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Application.Commands;

internal sealed class BootstrapOwnerCommandValidator : ICommandValidator<BootstrapOwnerCommand>
{
    public IEnumerable<string> Validate(BootstrapOwnerCommand command)
    {
        if (!AccessSubject.TryCreate(AccessSubjectKind.AdminActor, command.ActorId, out _))
        {
            yield return AccessControlApplicationErrors.SubjectInvalid.Message;
        }
    }
}
