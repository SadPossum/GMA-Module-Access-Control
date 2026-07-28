namespace Gma.Modules.AccessControl.Application;

using System.Diagnostics.CodeAnalysis;
using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Contracts;
using Microsoft.Extensions.Logging;

internal sealed partial class RoleAssignmentLifecycleOutcomeObserver(
    IEnumerable<IAccessRoleAssignmentLifecycleObserver> observers,
    ILogger<RoleAssignmentLifecycleOutcomeObserver> logger)
    : ICommandOutcomeObserver<AssignRoleCommand, Unit>,
      ICommandOutcomeObserver<UnassignRoleCommand, Unit>
{
    public async Task ObserveAsync(
        AssignRoleCommand command,
        Result<Unit> result,
        CancellationToken cancellationToken)
    {
        if (!TryCreateContext(
                command.SubjectKind,
                command.SubjectId,
                command.RoleName,
                command.AccessScope,
                command.ExpiresAtUtc,
                out AccessSubject? subject,
                out string? roleName,
                out AccessScope? accessScope,
                out DateTimeOffset? expiresAtUtc))
        {
            return;
        }

        Guid correlationId = Guid.CreateVersion7();
        await this.NotifyAsync(
            new AccessRoleAssignmentLifecycleEvent(
                AccessRoleAssignmentLifecycleStage.Requested,
                subject,
                roleName,
                accessScope,
                expiresAtUtc,
                correlationId),
            cancellationToken).ConfigureAwait(false);
        await this.NotifyAsync(
            new AccessRoleAssignmentLifecycleEvent(
                result.IsSuccess
                    ? AccessRoleAssignmentLifecycleStage.Granted
                    : AccessRoleAssignmentLifecycleStage.Denied,
                subject,
                roleName,
                accessScope,
                expiresAtUtc,
                correlationId),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task ObserveAsync(
        UnassignRoleCommand command,
        Result<Unit> result,
        CancellationToken cancellationToken)
    {
        if (!result.IsSuccess ||
            !TryCreateContext(
                command.SubjectKind,
                command.SubjectId,
                command.RoleName,
                command.AccessScope,
                expiresAtUtc: null,
                out AccessSubject? subject,
                out string? roleName,
                out AccessScope? accessScope,
                out _))
        {
            return;
        }

        await this.NotifyAsync(
            new AccessRoleAssignmentLifecycleEvent(
                AccessRoleAssignmentLifecycleStage.Revoked,
                subject,
                roleName,
                accessScope,
                expiresAtUtc: null,
                Guid.CreateVersion7()),
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask NotifyAsync(
        AccessRoleAssignmentLifecycleEvent lifecycleEvent,
        CancellationToken cancellationToken)
    {
        foreach (IAccessRoleAssignmentLifecycleObserver observer in observers)
        {
            try
            {
                await observer
                    .ObserveAsync(lifecycleEvent, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                TryLogLifecycleObserverFailure(
                    logger,
                    observer.GetType().FullName ?? observer.GetType().Name,
                    lifecycleEvent.Stage);
            }
        }
    }

    private static void TryLogLifecycleObserverFailure(
        ILogger logger,
        string observerType,
        AccessRoleAssignmentLifecycleStage stage)
    {
        try
        {
            LogLifecycleObserverFailure(logger, observerType, stage);
        }
        catch (Exception)
        {
            // Lifecycle observation is post-settlement; logging cannot interrupt remaining observers.
        }
    }

    [LoggerMessage(
        EventId = 5101,
        Level = LogLevel.Warning,
        Message = "Role-assignment lifecycle observer {ObserverType} failed for stage {Stage}.")]
    private static partial void LogLifecycleObserverFailure(
        ILogger logger,
        string observerType,
        AccessRoleAssignmentLifecycleStage stage);

    private static bool TryCreateContext(
        AccessSubjectKind subjectKind,
        string subjectId,
        string roleNameValue,
        AccessScope? accessScopeValue,
        DateTimeOffset? expiresAtUtc,
        [NotNullWhen(true)] out AccessSubject? subject,
        [NotNullWhen(true)] out string? roleName,
        [NotNullWhen(true)] out AccessScope? accessScope,
        out DateTimeOffset? normalizedExpiry)
    {
        subject = null;
        roleName = null;
        accessScope = accessScopeValue ?? AccessScope.Global;
        normalizedExpiry = expiresAtUtc?.ToUniversalTime();
        if (!AccessSubject.TryCreate(subjectKind, subjectId, out subject))
        {
            return false;
        }

        return AccessControlRoleName.TryNormalize(roleNameValue, out roleName);
    }
}
