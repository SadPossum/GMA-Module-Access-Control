namespace Gma.Modules.AccessControl.Application;

using Gma.Framework.Results;
using Gma.Modules.AccessControl.Contracts;
using Microsoft.Extensions.Logging;

internal sealed partial class AccessProfileMutationAdmissionPolicy(
    IEnumerable<IAccessProfileMutationAdmissionPolicy> policies,
    ILogger<AccessProfileMutationAdmissionPolicy> logger)
{
    public async ValueTask<Result> AuthorizeAsync(
        AccessProfileMutationAdmissionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (IAccessProfileMutationAdmissionPolicy policy in policies)
        {
            AccessProfileMutationAdmissionDecision decision;
            try
            {
                decision = await policy.EvaluateAsync(
                    context,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogPolicyFailure(
                    logger,
                    policy.GetType().FullName,
                    context.Operation,
                    exception.GetType().Name);
                return Result.Failure(
                    AccessControlApplicationErrors
                        .ProfileMutationAdmissionUnavailable);
            }

            if (decision is AccessProfileMutationAdmissionDecision.Denied)
            {
                return Result.Failure(
                    AccessControlApplicationErrors.ProfileMutationRejected);
            }

            if (decision is not AccessProfileMutationAdmissionDecision.Allowed)
            {
                return Result.Failure(
                    AccessControlApplicationErrors
                        .ProfileMutationAdmissionUnavailable);
            }
        }

        return Result.Success();
    }

    [LoggerMessage(
        EventId = 5102,
        Level = LogLevel.Warning,
        Message = "Access-profile mutation admission policy {PolicyType} failed for operation {Operation} with {ExceptionType}.")]
    private static partial void LogPolicyFailure(
        ILogger logger,
        string? policyType,
        AccessProfileMutationAdmissionOperation operation,
        string exceptionType);
}
