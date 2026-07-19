namespace Gma.Modules.AccessControl.Domain.Entities;

using System.Diagnostics.CodeAnalysis;
using Gma.Framework.Permissions;
using Gma.Modules.AccessControl.Domain.Aggregates;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Profile permission is the domain concept name.")]
public sealed class AccessProfilePermission
{
    private AccessProfilePermission() { }

    internal AccessProfilePermission(Guid profileId, PermissionCode permission, DateTimeOffset createdAtUtc)
    {
        this.ProfileId = profileId;
        this.PermissionCode = permission.Value;
        this.CreatedAtUtc = createdAtUtc;
    }

    public Guid ProfileId { get; private set; }
    public string PermissionCode { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public AccessProfile? Profile { get; private set; }
}
