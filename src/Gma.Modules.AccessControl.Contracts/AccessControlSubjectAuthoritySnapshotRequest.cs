namespace Gma.Modules.AccessControl.Contracts;

using Gma.Framework.AccessControl;

public sealed record AccessControlSubjectAuthoritySnapshotRequest(
    AccessSubjectKind SubjectKind,
    string SubjectId,
    AccessScope RootScope,
    IReadOnlyCollection<AccessScope> ExpectedTargetScopes);
