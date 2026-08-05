# Access-Control Scope Lifecycle

Status: completed
Date: 2026-08-05

## Goal

Provide a product-neutral, contracts-only facade that can snapshot, export,
close, and incrementally erase all AccessControl state owned by one non-global
access-scope subtree.

The lifecycle must reject late grants and transport messages after closure,
remain idempotent across retries, and keep BunkFy workspace terminology and
termination orchestration outside the module.

## Ownership

The selected scope owns:

- compatibility-role assignments at the root scope or any descendant;
- access profiles whose owner scope is the root or a descendant, including
  their permissions, assignments, and immutable change history;
- profile assignments into the subtree even when the profile owner is outside
  it;
- assignment-history rows that reference the subtree even when their profile
  owner is outside it; and
- inbox rows carrying the explicitly bound transport scope id.

Global role definitions, role permissions, bootstrap state, and principals
still referenced by another assignment are not scope-owned. A principal may be
removed only after no role or profile assignment references it anywhere.

## Contract

- Publish `IAccessControlScopeLifecycle` and its request/result records from
  `Gma.Modules.AccessControl.Contracts`.
- Address a lifecycle with two explicit, product-neutral coordinates: a
  canonical non-global `AccessScope` root and a normalized transport scope id.
- Export deterministic, bounded pages for role assignments, profiles with
  permission snapshots, profile assignments, and profile changes.
- Denormalize the global role name and ordered permission snapshot into each
  role-assignment export record without treating the role definition as
  scope-owned.
- Return typed invalid, missing, open, closed, stale, busy, conflict, replay,
  in-progress, and completed outcomes; do not expose provider exceptions as
  lifecycle decisions.

## Consistency

- Reuse AccessControl's provider-backed management lock and global management
  revision. This intentionally lets an unrelated AccessControl write stale an
  export, which is conservative and acceptable because management mutations
  are comparatively rare.
- Recheck the selected revision after every export page.
- Close the scope under the same management lock. Persist the resulting close
  revision so later destruction batches do not depend on unrelated global
  revisions.
- Return the durable selected revision with a closed snapshot so exact retries
  remain possible when a missing scope closes after unrelated global writes.
- Allow revocation, unassignment, and other access-reducing writes after
  closure, but reject new role assignments, new profile assignments, and
  profile creation or mutation at the root or any descendant.
- Bind one transport scope id to one access-scope root and suppress late inbox
  work after closure.

## Destruction

One call removes at most one bounded, non-empty batch and advances through:

1. transport inbox rows;
2. profile history owned by or referencing the subtree;
3. profile assignments owned by or targeting the subtree;
4. compatibility-role assignments in the subtree;
5. subtree-owned profiles and their cascaded children;
6. principals that became globally orphaned; and
7. a durable completion receipt.

An actively processing inbox row returns `Busy`. A completed operation replays
its receipt only for the exact operation id, coordinate, selected revision,
and batch size. Scope state, transport binding, and the bounded completion
receipt remain as the closure tombstone.

## Boundaries

- AccessControl does not know tenants, workspaces, properties, termination
  cases, approvals, or product data-export schemas.
- A product adapter maps its tenant coordinate to `AccessScope` and transport
  scope values and translates generic export records.
- Auth accounts remain global subject-owned data and are not erased by this
  lifecycle.
- Tenancy remains scope plumbing; it is not treated as an authoritative data
  owner merely because AccessControl uses scoped values.

## Acceptance

- exact-root and descendant data are exported once with stable cursors;
- cross-owned assignment and history references are included and erased;
- export detects every concurrent AccessControl management mutation;
- closure wins against concurrent grant/profile/inbox writes;
- removal paths remain usable while additions fail closed;
- destruction is bounded, resumable, conflict-safe, and receipt-backed;
- PostgreSQL and SQL Server migrations remain aligned; and
- focused unit tests, migration drift checks, and one exact PostgreSQL
  lifecycle scenario pass.

## Verification

Completed on 2026-08-05 with:

- AccessControl boundary checks passing;
- a warning-free integration-test project build;
- 140 fast AccessControl tests passing with no skips;
- PostgreSQL and SQL Server migration-drift checks passing; and
- one exact PostgreSQL lifecycle scenario passing with no skip, covering the
  latest migration, paged cross-owned export, batch-size-one destruction,
  orphan-principal safety, closed-scope write admission, late inbox
  suppression, exact replay/conflict behavior, immutable closed state, and
  append-only receipt protection.

The relational lifecycle now also constrains close coordinates, bounded
operation progress, terminal receipt progress, proof version, and timestamps
on both providers. Provider-specific triggers prevent reopening a closed scope
or mutating/deleting its terminal receipt. The EF models declare both
trigger-backed tables so SQL Server state updates and receipt inserts use
trigger-compatible DML.
