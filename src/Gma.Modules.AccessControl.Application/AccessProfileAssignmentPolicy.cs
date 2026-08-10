namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Microsoft.Extensions.Logging;

internal sealed partial class AccessProfileAssignmentPolicy(
    IEnumerable<IAccessProfileAssignmentPolicy> policies,
    ILogger<AccessProfileAssignmentPolicy> logger)
{
    public Task<bool> IsAllowedAsync(
        AccessProfile profile,
        AccessScope ownerScope,
        AccessSubject actor,
        AccessSubject subject,
        CancellationToken cancellationToken) =>
        this.IsAllowedAsync(profile, ownerScope, ownerScope, actor, subject, cancellationToken);

    public async Task<bool> IsAllowedAsync(
        AccessProfile profile,
        AccessScope ownerScope,
        AccessScope assignmentScope,
        AccessSubject actor,
        AccessSubject subject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        AccessProfileAssignmentPolicyContext context = new(
            profile.Id,
            profile.Key,
            ownerScope,
            actor,
            subject,
            profile.Permissions.Select(permission => permission.PermissionCode).ToArray(),
            assignmentScope);
        foreach (IAccessProfileAssignmentPolicy policy in policies)
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
        EventId = 5105,
        Level = LogLevel.Warning,
        Message = "Access-profile assignment policy {PolicyType} failed with {ExceptionType}.")]
    private static partial void LogPolicyFailure(
        ILogger logger,
        string? policyType,
        string exceptionType);
}
