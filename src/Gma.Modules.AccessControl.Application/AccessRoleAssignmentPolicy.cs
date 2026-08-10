namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Contracts;
using Microsoft.Extensions.Logging;

internal sealed partial class AccessRoleAssignmentPolicy(
    IEnumerable<IAccessRoleAssignmentPolicy> policies,
    ILogger<AccessRoleAssignmentPolicy> logger)
{
    public async Task<bool> IsAllowedAsync(
        AccessSubject subject,
        string roleName,
        AccessScope accessScope,
        DateTimeOffset? expiresAtUtc,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken)
    {
        AccessRoleAssignmentPolicyContext context = new(
            subject,
            roleName,
            accessScope,
            expiresAtUtc,
            permissions);
        foreach (IAccessRoleAssignmentPolicy policy in policies)
        {
            try
            {
                if (!await policy.IsAllowedAsync(context, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogPolicyFailure(
                    logger,
                    policy.GetType().FullName,
                    exception.GetType().Name);
                return false;
            }
        }

        return true;
    }

    [LoggerMessage(
        EventId = 5104,
        Level = LogLevel.Warning,
        Message = "Role-assignment policy {PolicyType} failed with {ExceptionType}.")]
    private static partial void LogPolicyFailure(
        ILogger logger,
        string? policyType,
        string exceptionType);
}
