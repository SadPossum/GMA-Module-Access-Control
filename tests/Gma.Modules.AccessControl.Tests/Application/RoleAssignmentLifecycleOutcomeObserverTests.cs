namespace Gma.Modules.AccessControl.Tests.Application;

using Gma.Framework.AccessControl;
using Gma.Framework.Cqrs;
using Gma.Framework.Results;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Commands;
using Gma.Modules.AccessControl.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class RoleAssignmentLifecycleOutcomeObserverTests
{
    private static readonly AccessScope Scope = AccessScope.Parse("tenant:tenant-a");

    [Fact]
    public async Task Successful_grant_reports_requested_then_granted_after_settlement()
    {
        RecordingLifecycleObserver lifecycle = new();
        RoleAssignmentLifecycleOutcomeObserver observer = CreateObserver(lifecycle);
        DateTimeOffset expiresAtUtc = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);
        AssignRoleCommand command = new(
            AccessSubjectKind.AdminActor,
            "support-a",
            " Support-Viewer ",
            Scope,
            expiresAtUtc);

        await observer.ObserveAsync(
            command,
            Result.Success(Unit.Value),
            CancellationToken.None);

        Assert.Collection(
            lifecycle.Events,
            item => Assert.Equal(AccessRoleAssignmentLifecycleStage.Requested, item.Stage),
            item => Assert.Equal(AccessRoleAssignmentLifecycleStage.Granted, item.Stage));
        Assert.All(lifecycle.Events, item =>
        {
            Assert.Equal(AccessSubjectKind.AdminActor, item.Subject.Kind);
            Assert.Equal("support-viewer", item.RoleName);
            Assert.Equal(Scope, item.AccessScope);
            Assert.Equal(expiresAtUtc, item.ExpiresAtUtc);
        });
        Assert.NotEqual(Guid.Empty, lifecycle.Events[0].CorrelationId);
        Assert.Equal(lifecycle.Events[0].CorrelationId, lifecycle.Events[1].CorrelationId);
    }

    [Fact]
    public async Task Failed_grant_reports_requested_then_denied()
    {
        RecordingLifecycleObserver lifecycle = new();
        RoleAssignmentLifecycleOutcomeObserver observer = CreateObserver(lifecycle);
        AssignRoleCommand command = new(
            AccessSubjectKind.AdminActor,
            "support-a",
            "support-viewer",
            AccessScope.Global);

        await observer.ObserveAsync(
            command,
            Result.Failure<Unit>(new Error("AccessControl.AssignmentRejected", "Rejected.")),
            CancellationToken.None);

        Assert.Equal(
            [
                AccessRoleAssignmentLifecycleStage.Requested,
                AccessRoleAssignmentLifecycleStage.Denied
            ],
            lifecycle.Events.Select(item => item.Stage));
    }

    [Fact]
    public async Task Revocation_is_reported_only_after_success()
    {
        RecordingLifecycleObserver lifecycle = new();
        RoleAssignmentLifecycleOutcomeObserver observer = CreateObserver(lifecycle);
        UnassignRoleCommand command = new(
            AccessSubjectKind.AdminActor,
            "support-a",
            "support-viewer",
            Scope);

        await observer.ObserveAsync(
            command,
            Result.Failure<Unit>(new Error("AccessControl.AssignmentNotFound", "Not found.")),
            CancellationToken.None);
        Assert.Empty(lifecycle.Events);

        await observer.ObserveAsync(
            command,
            Result.Success(Unit.Value),
            CancellationToken.None);

        AccessRoleAssignmentLifecycleEvent lifecycleEvent = Assert.Single(lifecycle.Events);
        Assert.Equal(AccessRoleAssignmentLifecycleStage.Revoked, lifecycleEvent.Stage);
        Assert.NotEqual(Guid.Empty, lifecycleEvent.CorrelationId);
    }

    [Fact]
    public async Task Invalid_unclassified_input_is_not_observed()
    {
        RecordingLifecycleObserver lifecycle = new();
        RoleAssignmentLifecycleOutcomeObserver observer = CreateObserver(lifecycle);
        AssignRoleCommand command = new(
            AccessSubjectKind.AdminActor,
            "support-a",
            "not a role",
            Scope);

        await observer.ObserveAsync(
            command,
            Result.Failure<Unit>(new Error("AccessControl.RoleNameInvalid", "Invalid.")),
            CancellationToken.None);

        Assert.Empty(lifecycle.Events);
    }

    [Fact]
    public async Task One_lifecycle_observer_failure_does_not_hide_the_fact_from_others()
    {
        RecordingLifecycleObserver recording = new();
        RoleAssignmentLifecycleOutcomeObserver observer = CreateObserver(
            new ThrowingLifecycleObserver(),
            recording);

        await observer.ObserveAsync(
            new AssignRoleCommand(
                AccessSubjectKind.AdminActor,
                "support-a",
                "support-viewer",
                Scope),
            Result.Success(Unit.Value),
            CancellationToken.None);

        Assert.Equal(2, recording.Events.Count);
    }

    [Fact]
    public async Task Logging_failure_does_not_hide_the_fact_from_other_lifecycle_observers()
    {
        RecordingLifecycleObserver recording = new();
        RoleAssignmentLifecycleOutcomeObserver observer = new(
            [new ThrowingLifecycleObserver(), recording],
            new ThrowingLogger<RoleAssignmentLifecycleOutcomeObserver>());

        await observer.ObserveAsync(
            new AssignRoleCommand(
                AccessSubjectKind.AdminActor,
                "support-a",
                "support-viewer",
                Scope),
            Result.Success(Unit.Value),
            CancellationToken.None);

        Assert.Equal(2, recording.Events.Count);
    }

    private static RoleAssignmentLifecycleOutcomeObserver CreateObserver(
        params IAccessRoleAssignmentLifecycleObserver[] observers) =>
        new(
            observers,
            NullLogger<RoleAssignmentLifecycleOutcomeObserver>.Instance);

    private sealed class RecordingLifecycleObserver : IAccessRoleAssignmentLifecycleObserver
    {
        public List<AccessRoleAssignmentLifecycleEvent> Events { get; } = [];

        public ValueTask ObserveAsync(
            AccessRoleAssignmentLifecycleEvent lifecycleEvent,
            CancellationToken cancellationToken = default)
        {
            this.Events.Add(lifecycleEvent);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingLifecycleObserver : IAccessRoleAssignmentLifecycleObserver
    {
        public ValueTask ObserveAsync(
            AccessRoleAssignmentLifecycleEvent lifecycleEvent,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new InvalidOperationException("observer unavailable"));
    }

    private sealed class ThrowingLogger<TCategory> : ILogger<TCategory>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            throw new InvalidOperationException("logger unavailable");
    }
}
