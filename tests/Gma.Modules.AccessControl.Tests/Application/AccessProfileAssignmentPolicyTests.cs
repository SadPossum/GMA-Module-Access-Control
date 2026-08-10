namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Contracts;
using Gma.Modules.AccessControl.Domain.Aggregates;
using Gma.Modules.AccessControl.Domain.Enums;
using Gma.Modules.AccessControl.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessProfileAssignmentPolicyTests
{
    private static readonly AccessScope Scope = AccessScope.Parse("tenant:tenant-a");
    private static readonly AccessScope PropertyScope =
        AccessScope.Parse("tenant:tenant-a/property:property-a");
    private static readonly AccessSubject Actor = AccessSubject.User("actor-a");
    private static readonly AccessSubject Subject = AccessSubject.User("subject-a");

    [Theory]
    [InlineData("tenant:tenant-a", "tenant:tenant-a", true)]
    [InlineData("tenant:tenant-a", "tenant:tenant-a/property:property-a", true)]
    [InlineData("tenant:tenant-a/property:property-a", "tenant:tenant-a", false)]
    [InlineData("tenant:tenant-a", "tenant:tenant-b/property:property-a", false)]
    [InlineData("tenant:tenant-a", "global", false)]
    [InlineData("global", "tenant:tenant-a", false)]
    public void Assignment_scope_must_be_the_non_global_owner_scope_or_its_descendant(
        string ownerScope,
        string assignmentScope,
        bool expected)
    {
        Assert.Equal(
            expected,
            AccessProfileAssignmentScopePolicy.Validate(
                AccessScope.Parse(ownerScope),
                AccessScope.Parse(assignmentScope)).IsSuccess);
    }

    [Fact]
    public async Task Assignment_policy_allows_when_no_product_policy_is_registered()
    {
        AccessProfileAssignmentPolicy policy = new(
            [],
            NullLogger<AccessProfileAssignmentPolicy>.Instance);

        bool allowed = await policy.IsAllowedAsync(
            CreateProfile(), Scope, PropertyScope, Actor, Subject, CancellationToken.None);

        Assert.True(allowed);
    }

    [Fact]
    public async Task Assignment_policy_passes_stable_context_and_stops_on_first_rejection()
    {
        RecordingPolicy allowing = new(true);
        RecordingPolicy denying = new(false);
        RecordingPolicy unreachable = new(true);
        AccessProfileAssignmentPolicy policy = new(
            [allowing, denying, unreachable],
            NullLogger<AccessProfileAssignmentPolicy>.Instance);

        bool allowed = await policy.IsAllowedAsync(
            CreateProfile(), Scope, PropertyScope, Actor, Subject, CancellationToken.None);

        Assert.False(allowed);
        AccessProfileAssignmentPolicyContext context = Assert.Single(allowing.Contexts);
        Assert.Equal("front-desk", context.ProfileKey);
        Assert.Equal(Scope, context.OwnerScope);
        Assert.Equal(PropertyScope, context.AssignmentScope);
        Assert.Equal(Actor, context.Actor);
        Assert.Equal(Subject, context.Subject);
        Assert.Equal(["reservations.read"], context.Permissions);
        IList<string> permissions = Assert.IsAssignableFrom<IList<string>>(context.Permissions);
        Assert.Throws<NotSupportedException>(() => permissions[0] = "reservations.write");
        Assert.Single(denying.Contexts);
        Assert.Empty(unreachable.Contexts);
    }

    [Fact]
    public async Task Assignment_policy_failure_denies_and_stops_the_pipeline()
    {
        RecordingPolicy unreachable = new(true);
        AccessProfileAssignmentPolicy policy = new(
            [new ThrowingPolicy(), unreachable],
            NullLogger<AccessProfileAssignmentPolicy>.Instance);

        bool allowed = await policy.IsAllowedAsync(
            CreateProfile(), Scope, PropertyScope, Actor, Subject, CancellationToken.None);

        Assert.False(allowed);
        Assert.Empty(unreachable.Contexts);
    }

    [Fact]
    public async Task Assignment_policy_preserves_caller_cancellation()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        AccessProfileAssignmentPolicy policy = new(
            [new CancelingPolicy()],
            NullLogger<AccessProfileAssignmentPolicy>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => policy.IsAllowedAsync(
            CreateProfile(), Scope, PropertyScope, Actor, Subject, cancellation.Token));
    }

    private static AccessProfile CreateProfile()
    {
        Gma.Framework.Results.Result<AccessProfile> result = AccessProfile.Create(
            Guid.NewGuid(),
            Scope.Value,
            "front-desk",
            "Front desk",
            null,
            ["reservations.read"],
            new AccessProfileSubject(AccessProfileSubjectKind.User, Actor.Id),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private sealed class RecordingPolicy(bool allowed) : IAccessProfileAssignmentPolicy
    {
        public List<AccessProfileAssignmentPolicyContext> Contexts { get; } = [];

        public ValueTask<bool> IsAllowedAsync(
            AccessProfileAssignmentPolicyContext context,
            CancellationToken cancellationToken)
        {
            this.Contexts.Add(context);
            return ValueTask.FromResult(allowed);
        }
    }

    private sealed class ThrowingPolicy : IAccessProfileAssignmentPolicy
    {
        public ValueTask<bool> IsAllowedAsync(
            AccessProfileAssignmentPolicyContext context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Private product policy failure.");
    }

    private sealed class CancelingPolicy : IAccessProfileAssignmentPolicy
    {
        public ValueTask<bool> IsAllowedAsync(
            AccessProfileAssignmentPolicyContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromCanceled<bool>(cancellationToken);
    }
}
