namespace Gma.Modules.AccessControl.Contracts;

public sealed record AccessControlScopeSnapshot(
    AccessControlScopeStatus Status,
    long Revision)
{
    public long? SelectedRevision { get; init; }
}
