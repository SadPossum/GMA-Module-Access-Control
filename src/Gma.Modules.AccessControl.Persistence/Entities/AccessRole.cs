namespace Gma.Modules.AccessControl.Persistence.Entities;

using Gma.Modules.AccessControl.Application;

public sealed class AccessRole
{
    private readonly List<AccessRolePermission> permissions = [];
    private readonly List<AccessSubjectRoleAssignment> assignments = [];

    private AccessRole() { }

    public AccessRole(Guid id, string name, DateTimeOffset createdAtUtc)
    {
        this.Id = id;
        this.Name = AccessControlRoleName.Normalize(name);
        this.CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public IReadOnlyCollection<AccessRolePermission> Permissions => this.permissions;
    public IReadOnlyCollection<AccessSubjectRoleAssignment> Assignments => this.assignments;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static string NormalizeName(string name) => AccessControlRoleName.Normalize(name);
}
