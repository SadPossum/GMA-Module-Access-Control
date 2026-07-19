namespace Gma.Modules.AccessControl.Application.Validation;

using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Application.Commands;

internal sealed class AssignAccessProfileCommandValidator : ICommandValidator<AssignAccessProfileCommand>
{
    public IEnumerable<string> Validate(AssignAccessProfileCommand command) =>
        AccessProfileCommandValidation.ValidateProfileId(command.ProfileId)
            .Concat(AccessProfileCommandValidation.ValidateOwnerScope(command.OwnerScope))
            .Concat(AccessProfileCommandValidation.ValidateSubject(command.Subject))
            .Concat(AccessProfileCommandValidation.ValidateActor(command.Actor));
}
