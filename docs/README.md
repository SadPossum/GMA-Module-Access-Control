# AccessControl Module

Development tasks:

- [AccessControl production hardening](access-control-production-hardening-task.md)
- [AccessControl domain completion](access-control-domain-completion-task.md)

`Gma.Modules.AccessControl` is the optional persisted RBAC implementation for the generic access-control framework.

It owns:

- access principals keyed by subject kind and subject id;
- role names and role permissions;
- subject role assignments scoped by normalized `AccessScope`;
- tenant-owned scoped access profiles, profile permissions, assignments, and immutable change history;
- SQL Server and PostgreSQL migrations in the `access` schema;
- the persisted `IAccessDecisionProvider` used by `IAccessAuthorizationService`;
- the persisted `IAccessGrantScopeReader` used by modules that need grant scopes before building their own queries;
- optional admin CLI/API front doors for bootstrap and role management.
- an optional normal API front door for scoped profile management.

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

Products must explicitly register the permission codes that may appear in scoped profiles. The default allowlist is empty and profile mutations therefore fail closed:

```csharp
builder.Services.AddAccessProfilePermissionAllowlist([
    PropertiesPermissionCodes.Read,
    ReservationsPermissionCodes.Manage
]);
```

Registration only establishes eligibility. Create, update, and assignment operations also require the acting subject to hold every delegated permission in the profile's owning scope.
Delegation is evaluated through the framework batch authorization contract, so one profile operation does not perform one provider round trip per permission.

Products may additionally register one or more `IAccessProfileAssignmentPolicy` implementations when profile assignment eligibility depends on product-owned state. Policies run after AccessControl's delegation checks and before persistence; any rejection fails closed. AccessControl intentionally does not know what membership, employment, or another product lifecycle means.

Cross-module extensions may remove every scoped profile assignment for one subject and one exact scope through `IAccessProfileAssignmentRevoker`. The operation is transactional and idempotent, preserves immutable unassignment history, and does not affect compatibility roles or assignments in another scope. Lifecycle event handling belongs in an explicit extension, not in AccessControl.

Cross-module extensions and products that provision global compatibility roles use the Contracts-only facade:

```csharp
IAccessControlRoleProvisioner provisioner = ...;
await provisioner.EnsureRoleAsync(new AccessControlRoleDefinition(
    "workspace-manager",
    ["properties.read"]), cancellationToken);
await provisioner.EnsureAssignmentAsync(
    AccessSubject.User(subjectId),
    "workspace-manager",
    workspaceScope,
    cancellationToken);
```

Do not consume `IAccessControlRbacRepository` outside AccessControl. It is a persistence-shaped Application port, not a module contract.
Compatibility-role definitions supplied through the Contracts facade are reconciled exactly, so removing a seeded permission removes the persisted grant. Final-owner protection still rejects an unsafe wildcard removal.

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
admin roles revoke --actor <id> --role <role> --permission <code>
admin roles assign --actor <id> --target-kind <kind> --target-id <id> --role <role> [--scope <scope>]
admin roles unassign --actor <id> --target-kind <kind> --target-id <id> --role <role> [--scope <scope>]
admin roles assignments --actor <id> --role <role> [--page <n>] [--page-size <n>] [--output table|json]
admin roles list --actor <id> [--page <n>] [--page-size <n>] [--output table|json]
```

Supported target kinds are `user`, `admin-actor`, `service`, and `system`. `--target-actor` remains a compatibility alias for `--target-id`, with `admin-actor` as the CLI default. Subject kind is part of identity: `user/member-a` and `admin-actor/member-a` are distinct principals.

Existing assignment API clients may continue sending `actorId`; it is treated strictly as an `admin-actor` identity. New clients should send explicit `subjectKind` and `subjectId`, which are required for every non-admin subject.

Revocation and unassignment take effect immediately. The final global `admin-actor` owner wildcard assignment is protected from both unassignment and wildcard revocation so an operator cannot permanently lock every administration surface.

`bootstrap` creates the first owner principal and succeeds only when there are no existing assignments unless configuration explicitly allows bootstrap over existing assignments. The persisted implementation reserves bootstrap through a provider-backed singleton gate inside the same transaction as role/assignment creation, so concurrent first-owner attempts cannot both succeed.

## Admin API

AccessControl owns the compatibility role routes:

```text
GET  /api/admin/roles
POST /api/admin/roles
POST /api/admin/roles/{roleName}/permissions
DELETE /api/admin/roles/{roleName}/permissions/{permissionCode}
POST /api/admin/roles/{roleName}/assignments
GET  /api/admin/roles/{roleName}/assignments
DELETE /api/admin/roles/{roleName}/assignments?subjectKind=<kind>&subjectId=<id>&scope=<scope>
```

Bootstrap remains CLI-only.

All management list surfaces normalize page and page-size through framework `PageRequest`, order deterministically, and fetch one extra row for `HasMore` instead of issuing an unbounded count.

## Scoped Profile API

Normal API hosts compose `AccessControlApiModule`. Its routes are independent of Administration:

```text
GET    /api/access-control/profiles?scope=<scope>&page=<n>&pageSize=<n>
GET    /api/access-control/profiles/permissions?scope=<scope>
GET    /api/access-control/profiles/{profileId}?scope=<scope>
POST   /api/access-control/profiles?scope=<scope>
PUT    /api/access-control/profiles/{profileId}?scope=<scope>
POST   /api/access-control/profiles/{profileId}/archive?scope=<scope>
GET    /api/access-control/profiles/{profileId}/assignments?scope=<scope>&page=<n>&pageSize=<n>
POST   /api/access-control/profiles/{profileId}/assignments?scope=<scope>
DELETE /api/access-control/profiles/{profileId}/assignments?scope=<scope>&subjectKind=<kind>&subjectId=<id>
GET    /api/access-control/profiles/{profileId}/history?scope=<scope>&page=<n>&pageSize=<n>
```

Every route requires authentication and resolves a non-global owning scope before enforcing `access-control.profiles.read`, `access-control.profiles.manage`, or `access-control.profiles.assign`. Profile keys are unique only inside that owning scope. Archived profiles and their assignments remain queryable for traceability but stop authorizing immediately.

Profile update and archive requests carry an expected version. Provider concurrency and unique-key races are retried once through the CQRS pipeline so the second evaluation returns a stable application conflict rather than leaking a database exception.

Transactional AccessControl commands acquire the module's provider-backed management lock for the lifetime of their database transaction. Profile assignment eligibility, the assignment write, and exact-scope bulk revocation therefore cannot pass one another and leave a late grant behind. Product assignment policies must read current authoritative eligibility, and lifecycle handlers must make the subject ineligible before calling `IAccessProfileAssignmentRevoker`; a product that derives eligibility from compatibility membership assignments removes those assignments first.

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

The persisted provider keeps concrete permission grants exact-scope by default. A role granted `catalog.items.read` at `tenant:tenant-a` does not automatically inherit into `tenant:tenant-a/property:property-1`, and a global concrete grant does not automatically authorize tenant scopes. The bootstrap owner wildcard remains the compatibility escape hatch that uses global and ancestor scope matching.

Product modules still own the meaning of `property`, `region`, `department`, or any other resource segment. A permission may explicitly opt into descendant-scope inheritance in its descriptor:

```csharp
new ModulePermissionDescriptor(
    "properties.read",
    "Read visible properties.",
    PermissionScopeRequirement.Scoped,
    PermissionScopeGrantPolicy.Descendants)
```

The product module registers those policies explicitly during composition:

```csharp
builder.Services.AddGmaAccessControlPermissionPolicies(PropertiesModuleMetadata.Descriptor);
```

Registration is explicit and idempotent. Unregistered permissions remain exact-scope, and conflicting policies for the same permission code fail during service registration. `PermissionScopeGrantPolicy.Descendants` lets a tenant assignment cover matching property descendants but does not make a global assignment universal; global inheritance is a separate explicit policy.

For list/detail filtering, the intended split is:

```text
AccessControl
  -> granted AccessScope values for subject + permission

Owning module
  -> domain visibility policy
  -> typed query scope
  -> module-owned SQL predicate
```

Point authorization and `IAccessGrantScopeReader` resolve the same descriptor policy. Callers should use `AccessGrantScope.Grants(...)` when checking a requested scope in memory or translate the same exact/ancestor/global semantics deliberately in their own persistence layer.

## Boundaries

Modules declare permission codes in contracts/metadata. Roles are operator configuration and should not be hard-coded in product modules.

AccessControl references framework contracts and persistence helpers only. It must not reference Auth internals, Administration internals, NATS, Redis, or product modules.

Its persistence package owns a broker-neutral inbox. Cross-module extensions may therefore bind durable integration-event consumers to AccessControl without moving idempotency or transaction ownership into the producing module.

Point checks and profile-delegation checks share the same persisted authorization semantics. The persisted provider groups a batch by subject and scope, reads the union of relevant role and active-profile grants once per group, then applies each permission descriptor's scope policy in memory. Callers should still use `IAccessGrantScopeReader` and module-owned SQL predicates for large resource lists.

## Verification

Run the complete local gate with Docker available:

```powershell
./eng/verify.ps1
```

The gate checks architecture boundaries, a zero-warning build, SQL Server and PostgreSQL migration drift, fast tests, package vulnerabilities, and real SQL Server and PostgreSQL race/integration tests. Use `-SkipDocker` only for a deliberately reduced local pass; CI keeps both relational-provider proofs in a separate required Linux job.
