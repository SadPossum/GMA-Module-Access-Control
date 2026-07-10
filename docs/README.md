# AccessControl Module

`Gma.Modules.AccessControl` is the optional persisted RBAC implementation for the generic access-control framework.

It owns:

- access principals keyed by subject kind and subject id;
- role names and role permissions;
- subject role assignments scoped by normalized `AccessScope`;
- SQL Server and PostgreSQL migrations in the `access` schema;
- the persisted `IAccessDecisionProvider` used by `IAccessAuthorizationService`;
- the persisted `IAccessGrantScopeReader` used by modules that need grant scopes before building their own queries;
- optional admin CLI/API front doors for bootstrap and role management.

It does not own:

- user identity, passwords, sessions, or tokens;
- admin operation audit;
- product-specific visibility rules or resource graph semantics;
- product-specific HTTP, CLI, or UI front doors.

## Composition

Hosts that need persisted RBAC compose this module explicitly:

```csharp
builder.Services.AddAccessControlApplication(builder.Configuration);
builder.AddAccessControlPersistence();
```

Admin hosts compose the AccessControl admin front door explicitly:

```csharp
builder.AddAdminModule<AccessControlAdminCliModule>();
builder.AddAdminApiModule<AccessControlAdminApiModule>();
```

Those front doors register `Gma.Framework.Administration.AccessControl`, so admin authorization flows through generic access-control only when AccessControl is selected. `Gma.Modules.Administration` can still be composed by itself for audit storage and denies by default unless the host provides another `IAdminAuthorizationService`.

Normal API hosts can use `Gma.Framework.AccessControl.AspNetCore` endpoint helpers with this persisted provider without registering Administration front doors.

Modules that need bulk resource filtering should read granted access scopes once through `IAccessGrantScopeReader`, then translate those scopes inside the owning module's repository. Do not call `IAccessAuthorizationService` once per row. AccessControl still does not know what product concepts such as `region`, `property`, or `package` mean.

## CLI Commands

AccessControl owns the compatibility `admin` command group:

```text
admin bootstrap --actor <id> --yes
admin roles create --actor <id> --name <role>
admin roles grant --actor <id> --role <role> --permission <code>
admin roles assign --actor <id> --target-actor <id> --role <role> [--scope <scope>]
admin roles list --actor <id> [--output table|json]
```

`bootstrap` creates the first owner principal and succeeds only when there are no existing assignments unless configuration explicitly allows bootstrap over existing assignments. The persisted implementation reserves bootstrap through a provider-backed singleton gate inside the same transaction as role/assignment creation, so concurrent first-owner attempts cannot both succeed.

## Admin API

AccessControl owns the compatibility role routes:

```text
GET  /api/admin/roles
POST /api/admin/roles
POST /api/admin/roles/{roleName}/permissions
POST /api/admin/roles/{roleName}/assignments
```

Bootstrap remains CLI-only.

## Configuration

```json
{
  "AccessControl": {
    "Bootstrap": {
      "OwnerRoleName": "owner",
      "AllowWhenAssignmentsExist": false
    }
  }
}
```

`BootstrapOwnerCommand` creates the initial owner role and global owner assignment only when confirmed and when no assignments exist, unless `AllowWhenAssignmentsExist` is explicitly enabled. SQL Server and PostgreSQL migrations create the bootstrap gate; apply them before using the command.

## Scope Model

AccessControl answers this generic question:

```text
Can subject S perform permission P in scope X?
```

Scopes are normalized framework values such as:

- `global`
- `tenant:tenant-a`
- `tenant:tenant-a/property:property-1`

The persisted provider keeps concrete permission grants exact-scope by default. A role granted `catalog.items.read` at `tenant:tenant-a` does not automatically inherit into `tenant:tenant-a/property:property-1`, and a global concrete grant does not automatically authorize tenant scopes. The bootstrap owner wildcard is the compatibility escape hatch that may use global and ancestor scope matching.

Product modules still own the meaning of `property`, `region`, `department`, or any other resource segment. Descriptor-driven scope inheritance can be added later when a real product module needs per-permission inheritance semantics.

For list/detail filtering, the intended split is:

```text
AccessControl
  -> granted AccessScope values for subject + permission

Owning module
  -> domain visibility policy
  -> typed query scope
  -> module-owned SQL predicate
```

Concrete permission grants are exact scope grants. The bootstrap owner wildcard can produce grants with global/ancestor matching options, so callers should use `AccessGrantScope.Grants(...)` when checking a requested scope in memory or translate the same broad-scope semantics deliberately in their own persistence layer.

## Boundaries

Modules declare permission codes in contracts/metadata. Roles are operator configuration and should not be hard-coded in product modules.

AccessControl references framework contracts and persistence helpers only. It must not reference Auth internals, Administration internals, NATS, Redis, or product modules.
