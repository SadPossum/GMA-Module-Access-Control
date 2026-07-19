# AccessControl Production Hardening Task

Status: complete
Date: 2026-07-19

## Goal

Make the reusable AccessControl domain production-ready without moving identity, organization membership, product permission semantics, resource visibility, or administration audit into the module.

The existing global role model remains the compatibility and operator surface. This slice adds a separate scoped access-profile model for tenant-owned role definitions, closes the public integration boundary, bounds management queries, and proves security-critical behavior against PostgreSQL.

## Audit Baseline

- AccessControl references framework projects only and remains independent of Auth, Organizations, Administration module internals, products, NATS, and Redis;
- the persisted decision provider correctly applies exact, descendant, global, and owner-wildcard scope policies;
- bootstrap uses a singleton gate and serializable transaction;
- final global admin-owner removal is serialized through the module management lock;
- SQL Server and PostgreSQL migrations exist for global roles, assignments, bootstrap state, and the module-owned inbox;
- the zero-warning build, 33 local tests, and package vulnerability audit pass.

## Findings

1. Products and cross-module extensions consume `IAccessControlRbacRepository` from Application directly. That interface is persistence-shaped and bypasses the Contracts-only module boundary.
2. Role definitions are global even when assignments are scoped. Tenant-owned role names and permission sets therefore cannot be modeled safely without namespacing conventions leaking into products.
3. Role and assignment management lists are unbounded.
4. Concurrent create, grant, and assignment operations rely on check-then-write behavior; provider unique violations are not mapped to deterministic application results.
5. Bootstrap and final-owner protection have SQLite or in-memory coverage, but no real PostgreSQL race proof.
6. CI runs restore, build, and unit tests on Windows only. It has no boundary, migration-drift, vulnerability, or PostgreSQL integration gate.
7. AccessControl has no module-owned history for scoped security-policy changes. Administration audit remains separate and must not be treated as the domain record.

## Delivery Slices

### 1. Contracts-Only Integration Boundary

- add a narrow Contracts service for idempotent global role provisioning, assignment checks, scoped assignment discovery, and assignment removal;
- reconcile declared compatibility-role permissions exactly so removing a seeded grant cannot leave stale authority;
- implement the facade inside AccessControl and keep `IAccessControlRbacRepository` as an internal persistence port;
- move owner wildcard and public role-management result concepts needed by consumers into Contracts;
- align BunkFy extensions and host authorization code to the Contracts service;
- add boundary guards that reject external reusable-module or product dependencies in AccessControl source.

### 2. Scoped Access Profiles

- add a dedicated domain model with immutable profile id, immutable internal key, owning `AccessScope`, display name, description, active/archived state, and optimistic version;
- keep framework `AccessScope` and `AccessSubject` at the Application boundary; model persisted owner scope and profile actors with AccessControl-owned domain value objects;
- keep profiles separate from globally named compatibility roles;
- make profile keys unique only inside their owning scope so different tenants may use the same key or display name;
- own profile permissions and profile assignments in AccessControl persistence;
- reject the owner wildcard from profile permission sets;
- fail closed immediately when a profile is archived while retaining assignments and history for traceability;
- preserve exact subject-kind identity for user, admin actor, service, and system subjects.

### 3. Delegation And Product Policy

- require composing products to register an explicit allowlist of permission codes eligible for scoped profiles;
- require the acting subject to hold every permission it delegates at the profile's owning scope;
- expose scoped read, manage, and assign permission descriptors owned by AccessControl;
- authorize every profile API operation against the exact owning scope supplied to the operation;
- keep product role names, seed profiles, labels, summaries, and delegation restrictions outside GMA.

### 4. Lifecycle, History, And Concurrency

- support list, get, create, update, archive, assign, unassign, assignment list, and change-history operations;
- record immutable AccessControl domain change facts for profile and assignment mutations in the same transaction;
- use expected versions for profile update and archive;
- map optimistic concurrency and unique-constraint races to deterministic conflict results;
- retain archived profiles, assignments, and security history by default. Security-record erasure or retention requires a separate explicit policy.

### 5. Bounded Queries And Front Doors

- use framework `PageRequest` normalization for global roles, role assignments, profiles, profile assignments, and history;
- fetch one extra row to expose `HasMore` without an unbounded count;
- update Admin API and CLI role-list surfaces to accept bounded paging;
- add an optional normal API front door for scoped profiles without depending on Administration;
- expose status, history kind, and assignment-removal outcomes as string-serialized contract enums while keeping Domain types behind Application and Persistence;
- keep bootstrap and global compatibility-role mutation on the Administration front doors.

### 6. Persistence And Proof

- add provider-specific SQL Server and PostgreSQL migrations and supporting indexes;
- prove scope isolation, permission allowlisting, anti-escalation, archive fail-closed behavior, bounded queries, and contract facade behavior;
- add real PostgreSQL tests for concurrent bootstrap, concurrent final-owner removal, profile-version races, unique profile creation, and authorization visibility;
- add repository verification scripts for boundaries, migration drift, build, tests, Docker tests, and vulnerability audit;
- make Windows validation and Linux PostgreSQL jobs required CI surfaces.

### 7. Canonical Composition

- compose the optional scoped-profile API in GMA Skeleton and cover its deny-by-default allowlist behavior;
- align GMA Extensions against the Contracts-only seam if a cross-module consumer is present;
- register BunkFy's explicit eligible-permission allowlist without adding BunkFy permission names to GMA;
- update BunkFy access synchronization and notification audience readers to use Contracts only;
- move pins only after exact upstream and consumer verification is green.

## Ownership Boundaries

Framework continues to own:

- `AccessSubject`, `AccessScope`, permission descriptors, scope matching, and authorization composition;
- generic pagination and HTTP authorization primitives.

AccessControl owns:

- persisted global compatibility roles and assignments;
- scoped access-profile definitions, permissions, assignments, and security-policy history;
- deterministic authorization decisions and granted-scope discovery;
- module-owned persistence, inbox, API, administration API, and administration CLI front doors.

GMA Extensions owns:

- cross-module reactions that provision or remove access from another module's integration events.

Products own:

- permission allowlists and any stricter delegation policy;
- seed profiles, labels, role summaries, UI, and workflow terminology;
- membership and last-workspace-owner invariants;
- invitation profile selection and staff onboarding workflows;
- resource visibility beyond generic access scopes.

No Framework change is planned. Existing access-control, permission-descriptor, HTTP scope-resolution, CQRS, pagination, and persistence primitives cover this slice.

## Acceptance Criteria

- external product and extension code no longer references AccessControl Application repository ports;
- compatibility-role reconciliation removes stale permissions while preserving final-owner protection;
- two owning scopes can use the same profile key and display name without cross-scope reads, writes, assignments, or authorization;
- profile permissions are explicitly allowlisted, never include owner wildcard, and cannot exceed the actor's authority;
- archived profiles stop authorizing immediately and retain immutable change history;
- global and scoped management lists are bounded and deterministic;
- concurrent writes return stable conflicts rather than raw provider exceptions;
- final global admin-owner protection remains correct under PostgreSQL concurrency;
- SQL Server and PostgreSQL migrations are drift-free;
- boundary, zero-warning build, unit, package vulnerability, and PostgreSQL integration checks pass;
- Skeleton and BunkFy compose the published module without architecture regressions.

## Explicitly Deferred

- BunkFy workspace-role UI and product seed-profile UX;
- invitation-time profile selection and staff onboarding integration;
- organization membership or last-workspace-owner governance;
- attribute-based, relationship-based, or deny-rule authorization;
- resource-specific visibility graphs and row predicates;
- bulk import/export, approval workflows, temporary grants, or scheduled access;
- security-history retention, legal erasure, and external SIEM export;
- replacing global compatibility roles or removing the existing admin API and CLI surface.

## Current Verification

The production-hardening slice is complete and published through the canonical consumer chain:

- AccessControl `43b0f419d5803fd4146cd00d6bb0e7056cbae9be`: boundary checks, zero-warning build, drift-free SQL Server and PostgreSQL migrations, 66 fast tests, package vulnerability audit, and 6 real PostgreSQL integration tests; CI run `29680340936` passed;
- GMA Skeleton `619e748a3bce1a116a8bfec27bb09b3a8a386c3d`: canonical API composition, deny-by-default allowlist coverage, 263 architecture tests, and 16 integration/framework/module tests; CI run `29680540351` passed on Ubuntu and Windows;
- BunkFy backend `6c746ea1cee973a26b91cca37f05c43ac1215a05`: Contracts-only consumers, explicit delegation allowlist, architecture guards, 48 architecture tests, 19 integration tests, all fast tests, and 32 Docker tests; validation run `29681178978` and Docker run `29681178956` passed;
- BunkFy web `287499a509de3ed73b223f2a762934e63797886b`: generated AccessControl OpenAPI snapshot and TypeScript contracts; CI run `29682049875` passed;
- BunkFy root `52fb995f2a272eddd7a1a6a5919c6487b486ed20`: exact published submodule graph, generated workspace, backend verification, contract drift, frontend tests, and production build; CI run `29682081816` passed.

No GMA Extensions change was required because no cross-module extension consumed the AccessControl Application repository port. Product role-management UI and invitation-time profile selection remain explicitly deferred to BunkFy product slices.
