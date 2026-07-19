namespace Gma.Modules.AccessControl.Application.Validation;

using Gma.Framework.Cqrs;
using Gma.Modules.AccessControl.Application.Commands;

internal sealed class ArchiveAccessProfileCommandValidator : ICommandValidator<ArchiveAccessProfileCommand>
{
    public IEnumerable<string> Validate(ArchiveAccessProfileCommand command) =>
        AccessProfileCommandValidation.ValidateProfileId(command.ProfileId)
            .Concat(AccessProfileCommandValidation.ValidateOwnerScope(command.OwnerScope))
            .Concat(AccessProfileCommandValidation.ValidateActor(command.Actor))
            .Concat(AccessProfileCommandValidation.ValidateExpectedVersion(command.ExpectedVersion));
}
