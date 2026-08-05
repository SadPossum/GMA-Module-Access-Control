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
    [InlineData(AccessRoleAssignmentLifecycleStage.Requested, "requested")]
    [InlineData(AccessRoleAssignmentLifecycleStage.Granted, "granted")]
    [InlineData(AccessRoleAssignmentLifecycleStage.Denied, "denied")]
    [InlineData(AccessRoleAssignmentLifecycleStage.Revoked, "revoked")]
    public void Role_assignment_lifecycle_stage_uses_stable_wire_names(
        AccessRoleAssignmentLifecycleStage stage,
        string wireName) =>
        AssertWireValue(stage, wireName);

    [Theory]
    [InlineData(AccessRoleAssignmentStatus.Active, "active")]
    [InlineData(AccessRoleAssignmentStatus.Expired, "expired")]
    [InlineData(AccessRoleAssignmentStatus.Revoked, "revoked")]
    public void Role_assignment_status_uses_stable_wire_names(
        AccessRoleAssignmentStatus status,
        string wireName) =>
        AssertWireValue(status, wireName);

    [Theory]
    [InlineData(AccessProfileMutationAdmissionDecision.Allowed, "allowed")]
    [InlineData(AccessProfileMutationAdmissionDecision.Denied, "denied")]
    [InlineData(
        AccessProfileMutationAdmissionDecision.Unavailable,
        "unavailable")]
    public void Profile_mutation_admission_decision_uses_stable_wire_names(
        AccessProfileMutationAdmissionDecision decision,
        string wireName) =>
        AssertWireValue(decision, wireName);

    [Theory]
    [InlineData(
        AccessProfileMutationAdmissionOperation.CreateProfile,
        "create-profile")]
    [InlineData(
        AccessProfileMutationAdmissionOperation.UpdateProfile,
        "update-profile")]
    [InlineData(
        AccessProfileMutationAdmissionOperation.ArchiveProfile,
        "archive-profile")]
    [InlineData(
        AccessProfileMutationAdmissionOperation.EnsureProfile,
        "ensure-profile")]
    public void Profile_mutation_admission_operation_uses_stable_wire_names(
        AccessProfileMutationAdmissionOperation operation,
        string wireName) =>
        AssertWireValue(operation, wireName);

    [Theory]
    [InlineData(AccessControlScopeStatus.Invalid, "invalid")]
    [InlineData(AccessControlScopeStatus.Missing, "missing")]
    [InlineData(AccessControlScopeStatus.Open, "open")]
    [InlineData(AccessControlScopeStatus.Closed, "closed")]
    public void Scope_status_uses_stable_wire_names(
        AccessControlScopeStatus status,
        string wireName) =>
        AssertWireValue(status, wireName);

    [Theory]
    [InlineData(AccessControlScopeExportStatus.Invalid, "invalid")]
    [InlineData(AccessControlScopeExportStatus.Completed, "completed")]
    [InlineData(AccessControlScopeExportStatus.Missing, "missing")]
    [InlineData(AccessControlScopeExportStatus.Closed, "closed")]
    [InlineData(AccessControlScopeExportStatus.Stale, "stale")]
    public void Scope_export_status_uses_stable_wire_names(
        AccessControlScopeExportStatus status,
        string wireName) =>
        AssertWireValue(status, wireName);

    [Theory]
    [InlineData(AccessControlScopeExportStore.RoleAssignments, "role-assignments")]
    [InlineData(AccessControlScopeExportStore.Profiles, "profiles")]
    [InlineData(AccessControlScopeExportStore.ProfileAssignments, "profile-assignments")]
    [InlineData(AccessControlScopeExportStore.ProfileChanges, "profile-changes")]
    public void Scope_export_store_uses_stable_wire_names(
        AccessControlScopeExportStore store,
        string wireName) =>
        AssertWireValue(store, wireName);

    [Theory]
    [InlineData(AccessControlScopeDestroyStatus.Invalid, "invalid")]
    [InlineData(AccessControlScopeDestroyStatus.InProgress, "in-progress")]
    [InlineData(AccessControlScopeDestroyStatus.Completed, "completed")]
    [InlineData(AccessControlScopeDestroyStatus.Replayed, "replayed")]
    [InlineData(AccessControlScopeDestroyStatus.Stale, "stale")]
    [InlineData(AccessControlScopeDestroyStatus.Busy, "busy")]
    [InlineData(AccessControlScopeDestroyStatus.Conflict, "conflict")]
    public void Scope_destroy_status_uses_stable_wire_names(
        AccessControlScopeDestroyStatus status,
        string wireName) =>
        AssertWireValue(status, wireName);

    [Theory]
    [InlineData(AccessControlScopeDestructionStage.InboxMessages, "inbox-messages")]
    [InlineData(AccessControlScopeDestructionStage.ProfileChanges, "profile-changes")]
    [InlineData(AccessControlScopeDestructionStage.ProfileAssignments, "profile-assignments")]
    [InlineData(AccessControlScopeDestructionStage.RoleAssignments, "role-assignments")]
    [InlineData(AccessControlScopeDestructionStage.Profiles, "profiles")]
    [InlineData(AccessControlScopeDestructionStage.OrphanPrincipals, "orphan-principals")]
    [InlineData(AccessControlScopeDestructionStage.Completed, "completed")]
    public void Scope_destruction_stage_uses_stable_wire_names(
        AccessControlScopeDestructionStage stage,
        string wireName) =>
        AssertWireValue(stage, wireName);

    [Theory]
    [InlineData("1")]
    [InlineData("\"unknown\"")]
    [InlineData("\"future\"")]
    public void Contract_enums_reject_numeric_unknown_and_future_values(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccessProfileStatus>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccessProfileChangeKind>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccessControlAssignmentRemovalOutcome>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccessRoleAssignmentLifecycleStage>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccessRoleAssignmentStatus>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<
            AccessProfileMutationAdmissionDecision>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<
            AccessProfileMutationAdmissionOperation>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccessControlScopeStatus>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccessControlScopeExportStatus>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccessControlScopeExportStore>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccessControlScopeDestroyStatus>(json, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AccessControlScopeDestructionStage>(json, JsonOptions));
    }

    [Fact]
    public void Contract_enums_reject_unknown_writes()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(AccessProfileStatus.Unknown, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(AccessProfileChangeKind.Unknown, JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            AccessControlAssignmentRemovalOutcome.Unknown,
            JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            AccessRoleAssignmentLifecycleStage.Unknown,
            JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            AccessRoleAssignmentStatus.Unknown,
            JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            AccessProfileMutationAdmissionDecision.Unknown,
            JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            AccessProfileMutationAdmissionOperation.Unknown,
            JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            AccessControlScopeStatus.Unknown,
            JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            AccessControlScopeExportStatus.Unknown,
            JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            AccessControlScopeExportStore.Unknown,
            JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            AccessControlScopeDestroyStatus.Unknown,
            JsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize(
            AccessControlScopeDestructionStage.Unknown,
            JsonOptions));
    }

    private static void AssertWireValue<T>(T value, string wireName)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        Assert.Equal($"\"{wireName}\"", json);
        Assert.Equal(value, JsonSerializer.Deserialize<T>(json, JsonOptions));
    }
}
