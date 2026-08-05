namespace Gma.Modules.AccessControl.Tests;

using Gma.Framework.AccessControl;
using Gma.Modules.AccessControl.Application;
using Gma.Modules.AccessControl.Application.Ports;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessControlScopeWriteAdmissionTests
{
    [Fact]
    public async Task Admission_normalizes_one_bounded_batch_per_reader()
    {
        RecordingReader reader = new(allowed: true);
        AccessControlScopeWriteAdmission admission = new(
            [reader],
            NullLogger<AccessControlScopeWriteAdmission>.Instance);
        AccessScope tenant = AccessScope.Parse("tenant:tenant-a");
        AccessScope property = AccessScope.Parse(
            "tenant:tenant-a/property:property-a");

        bool allowed = await admission.AreOpenAsync(
            [property, tenant, property, AccessScope.Global],
            CancellationToken.None);

        Assert.True(allowed);
        IReadOnlyCollection<AccessScope> scopes = Assert.Single(reader.Calls);
        Assert.Equal(
            [tenant.Value, property.Value],
            scopes.Select(scope => scope.Value));
    }

    [Fact]
    public async Task Admission_fails_closed_when_a_reader_fails()
    {
        AccessControlScopeWriteAdmission admission = new(
            [new ThrowingReader()],
            NullLogger<AccessControlScopeWriteAdmission>.Instance);

        bool allowed = await admission.AreOpenAsync(
            [AccessScope.Parse("tenant:tenant-a")],
            CancellationToken.None);

        Assert.False(allowed);
    }

    private sealed class RecordingReader(bool allowed)
        : IAccessControlScopeWriteAdmissionReader
    {
        public List<IReadOnlyCollection<AccessScope>> Calls { get; } = [];

        public Task<bool> AreOpenAsync(
            IReadOnlyCollection<AccessScope> accessScopes,
            CancellationToken cancellationToken)
        {
            this.Calls.Add(accessScopes);
            return Task.FromResult(allowed);
        }
    }

    private sealed class ThrowingReader : IAccessControlScopeWriteAdmissionReader
    {
        public Task<bool> AreOpenAsync(
            IReadOnlyCollection<AccessScope> accessScopes,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unavailable.");
    }
}
