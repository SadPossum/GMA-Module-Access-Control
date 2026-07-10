namespace Gma.Modules.AccessControl.Persistence.Entities;

internal sealed class AccessBootstrapState
{
    public const int SingletonId = 1;

    private AccessBootstrapState() { }

    public int Id { get; private set; } = SingletonId;
    public string? ClaimedBy { get; private set; }
    public DateTimeOffset? ClaimedAtUtc { get; private set; }
    public long ManagementRevision { get; private set; }
}
