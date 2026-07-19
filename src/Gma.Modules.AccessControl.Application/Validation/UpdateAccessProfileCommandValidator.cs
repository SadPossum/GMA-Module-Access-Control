namespace Gma.Modules.AccessControl.Application.Validation;

using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Application.Commands;

internal sealed class UpdateAccessProfileCommandValidator : ICommandValidator<UpdateAccessProfileCommand>
{
    public IEnumerable<string> Validate(UpdateAccessProfileCommand command) =>
        AccessProfileCommandValidation.ValidateProfileId(command.ProfileId)
            .Concat(AccessProfileCommandValidation.ValidateOwnerScope(command.OwnerScope))
            .Concat(AccessProfileCommandValidation.ValidateActor(command.Actor))
            .Concat(AccessProfileCommandValidation.ValidateExpectedVersion(command.ExpectedVersion))
            .Concat(AccessProfileCommandValidation.ValidateDefinition(
                key: null,
                command.DisplayName,
                command.Description,
                command.Permissions,
                validateKey: false));
}
