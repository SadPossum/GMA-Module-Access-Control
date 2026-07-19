namespace Gma.Modules.AccessControl.Tests;

using System.Text.Json;
using Gma.Modules.AccessControl.Contracts;
using Xunit;

[Trait("Category", "Unit")]
public sealed class AccessControlContractEnumJsonTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(AccessProfileStatus.Active, "active")]
    [InlineData(AccessProfileStatus.Archived, "archived")]
    public void Profile_status_uses_stable_wire_names(AccessProfileStatus status, string wireName) =>
        AssertWireValue(status, wireName);

    [Theory]
    [InlineData(AccessProfileChangeKind.Created, "created")]
    [InlineData(AccessProfileChangeKind.Updated, "updated")]
    [InlineData(AccessProfileChangeKind.Archived, "archived")]
    [InlineData(AccessProfileChangeKind.Assigned, "assigned")]
    [InlineData(AccessProfileChangeKind.Unassigned, "unassigned")]
    public void Profile_change_kind_uses_stable_wire_names(AccessProfileChangeKind kind, string wireName) =>
        AssertWireValue(kind, wireName);

    [Theory]
    [InlineData(AccessControlAssignmentRemovalOutcome.Removed, "removed")]
    [InlineData(AccessControlAssignmentRemovalOutcome.NotFound, "not-found")]
    [InlineData(AccessControlAssignmentRemovalOutcome.LastOwnerProtected, "last-owner-protected")]
    public void Assignment_removal_outcome_uses_stable_wire_names(
        AccessControlAssignmentRemovalOutcome outcome,
        string wireName) =>
        AssertWireValue(outcome, wireName);

    [Theory]
    [InlineData("1")]
    [InlineData("\"unknown\"")]
    [InlineData("\"future\"")]
    public void Contract_enums_reject_numeric_unknown_and_future_values(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccessProfileStatus>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccessProfileChangeKind>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccessControlAssignmentRemovalOutcome>(json, JsonOptions));
    }

    [Fact]
    public void Contract_enums_reject_unknown_writes()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(AccessProfileStatus.Unknown, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(AccessProfileChangeKind.Unknown, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            AccessControlAssignmentRemovalOutcome.Unknown,
            JsonOptions));
    }

    private static void AssertWireValue<T>(T value, string wireName)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        Assert.Equal($"\"{wireName}\"", json);
        Assert.Equal(value, JsonSerializer.Deserialize<T>(json, JsonOptions));
    }
}
