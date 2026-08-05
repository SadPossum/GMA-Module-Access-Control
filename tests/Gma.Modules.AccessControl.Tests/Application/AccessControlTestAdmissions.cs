namespace Gma.Modules.AccessControl.Tests;

using Gma.Modules.AccessControl.Application;
using Microsoft.Extensions.Logging.Abstractions;

internal static class AccessControlTestAdmissions
{
    public static AccessControlScopeWriteAdmission AllowAll() =>
        new(
            [],
            NullLogger<AccessControlScopeWriteAdmission>.Instance);
}
