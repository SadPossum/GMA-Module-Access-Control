namespace Gma.Modules.AccessControl.Application.Validation;

using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Application.Commands;

internal sealed class CreateAccessProfileCommandValidator : ICommandValidator<CreateAccessProfileCommand>
{
    public IEnumerable<string> Validate(CreateAccessProfileCommand command) =>
        AccessProfileCommandValidation.ValidateOwnerScope(command.OwnerScope)
            .Concat(AccessProfileCommandValidation.ValidateActor(command.Actor))
            .Concat(AccessProfileCommandValidation.ValidateDefinition(
                command.Key,
                command.DisplayName,
                command.Description,
                command.Permissions,
                validateKey: true));
}
