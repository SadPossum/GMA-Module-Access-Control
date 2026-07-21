namespace Gma.Modules.AccessControl.Application.Validation;

using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Application.Commands;

internal sealed class UnassignAccessProfileCommandValidator : ICommandValidator<UnassignAccessProfileCommand>
{
    public IEnumerable<string> Validate(UnassignAccessProfileCommand command) =>
        AccessProfileCommandValidation.ValidateProfileId(command.ProfileId)
            .Concat(AccessProfileCommandValidation.ValidateOwnerScope(command.OwnerScope))
            .Concat(AccessProfileCommandValidation.ValidateOwnerScope(command.AssignmentScope))
            .Concat(AccessProfileCommandValidation.ValidateSubject(command.Subject))
            .Concat(AccessProfileCommandValidation.ValidateActor(command.Actor));
}
