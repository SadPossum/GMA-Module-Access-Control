# Scoped Access-Profile Assignments Task

Status: completed
Date: 2026-07-21

## Goal

Allow one access profile owned at a non-global scope to be assigned at that scope or any descendant scope, without coupling AccessControl to a product resource model. Existing callers that assign and reconcile profiles at the owner scope must keep their behavior.

For example, a profile owned by `tenant:acme` may be assigned at `tenant:acme`, `tenant:acme/property:a`, or `tenant:acme/property:b`. It must never be assigned at `tenant:other`, an ancestor, or an unrelated branch.

## Invariants

- A profile definition remains owned by exactly one non-global `AccessScope`.
- Every assignment stores its own grant scope.
- An assignment scope must equal the profile owner scope or be its descendant according to the framework scope model.
- Effective grants use the assignment scope, never the profile owner scope.
- The same subject may receive the same profile at multiple valid assignment scopes.
- Assignment uniqueness is `(profile id, subject kind, subject id, assignment scope)`.
- Delegation is checked at the assignment scope; profile create and update delegation remains checked at the owner scope.
- Product eligibility policies receive both owner and assignment scope and remain fail-closed.
- Assignment and unassignment history records the assignment scope.
- Archived profiles stop authorizing at every assignment scope immediately.

## Contracts

- Preserve `IAccessProfileProvisioner` source behavior. Its assignment reads and reconciliation continue to operate only at the owner scope.
- Add a separate Contracts-only scoped assignment facade so existing custom `IAccessProfileProvisioner` implementations are not broken.
- Represent desired state as `(profile id, assignment scope)` targets and expose exact scoped assignment reads.
- Scoped full-set reconciliation owns all assignments for one subject whose profiles belong to the supplied owner scope. It validates every target before writing and applies the change transactionally.
- Keep `IAccessProfileAssignmentRevoker.RevokeAllAsync(subject, ownerScope, ...)` as the fail-closed operation that removes assignments at every grant scope for profiles owned by that scope.

## API And Operations

- Profile assignment creation accepts an optional assignment scope and defaults to the owner scope.
- Unassignment accepts the exact assignment scope and defaults to the owner scope for compatibility.
- Assignment list DTOs include the persisted assignment scope.
- Admin/CLI compatibility-role behavior is unchanged.
- Public route authorization still protects profile management at the owner scope; delegation and product policy then validate the requested grant scope.

## Persistence And Migration

- Add a required assignment-scope column bounded by `AccessScope.MaxLength`.
- Backfill every existing row from its profile owner scope before making the column required.
- Replace assignment uniqueness and lookup indexes with scope-aware indexes.
- Add an optional assignment-scope column to immutable profile change history and populate it for new assignment changes.
- Ship equivalent PostgreSQL and SQL Server migrations and prove migration drift is clean.

## Verification

- owner-scope compatibility behavior remains unchanged;
- the same profile can be assigned to one subject at two descendant scopes;
- sibling, ancestor, unrelated, and global assignment scopes are rejected;
- permission checks use the assignment scope and cannot escalate authority;
- assignment policies observe the exact assignment scope;
- effective authorization and grant-scope reads return the assignment scope;
- scoped unassignment removes only the exact target;
- legacy reconciliation leaves descendant-scope assignments untouched;
- scoped full-set reconciliation is idempotent and removes stale targets;
- bulk revocation removes all scopes under the profile owner boundary;
- archived profiles deny every scoped assignment;
- PostgreSQL and SQL Server migration/integration gates pass.

Completed evidence (2026-07-21):

- `eng/verify.ps1` passed architecture boundaries, restore, and a zero-warning build;
- PostgreSQL and SQL Server reported no pending model changes;
- all 84 fast tests passed;
- all 14 Docker-backed PostgreSQL and SQL Server integration/race tests passed;
- provider-specific upgrade tests preserved legacy assignments and backfilled the profile owner scope;
- the package vulnerability scan reported no vulnerable direct or transitive packages.

## Follow-Up Consumers

- BunkFy Workspaces stores immutable invitation profile/property plans and applies them through the scoped facade.
- BunkFy Staff lifecycle snapshots and restores `(profile id, assignment scope)` targets instead of profile ids alone.
- Product role names, property validity, employment eligibility, and invitation semantics remain outside GMA.
