# AccessControl Domain Completion Task

Status: in progress
Date: 2026-07-20

## Goal

Close the remaining production gaps in the reusable AccessControl domain without moving identity, organization membership, or product role semantics into Framework or the module.

The completed July 19 hardening slice remains the baseline. This task adds efficient batch authorization, an explicit product-policy seam for scoped-profile assignments, a Contracts-only way to revoke all profile grants for a subject in a scope, cross-module membership lifecycle alignment in GMA Extensions, and real SQL Server behavior proof.

## Verified Baseline

- AccessControl depends only on Framework projects and owns its domain, persistence, migrations, front doors, and security history;
- global compatibility roles and scoped access profiles are separate models whose grants are intentionally unioned by the persisted decision provider;
- profile definitions enforce a 100-permission bound, explicit product allowlisting, actor anti-escalation, optimistic versions, fail-closed archive behavior, and bounded reads;
- role reconciliation, transactional commands, and final global admin-owner removal are serialized through the module management lock;
- PostgreSQL proves bootstrap, final-owner protection, profile concurrency, scope isolation, and archive behavior;
- the current branch passes boundaries, a zero-warning build, SQL Server and PostgreSQL migration-drift checks, 70 fast tests, 11 real relational tests, and package vulnerability audit.

## Findings

1. Profile delegation authorizes each permission sequentially. A valid 100-permission profile can therefore cause 100 authorization-provider and database round trips while holding a request open.
2. AccessControl has no Contracts policy seam through which a product can reject a profile assignment. The generic API can assign a profile to any syntactically valid subject, while membership and subject eligibility correctly remain product concerns.
3. AccessControl has no Contracts operation for removing every scoped-profile assignment held by a subject in one owner scope. Cross-module membership suspension or removal can revoke compatibility roles while leaving custom profile grants active.
4. GMA Extensions has no Organizations-to-AccessControl lifecycle reaction for those scoped grants, despite that behavior being reusable cross-module coordination rather than product domain logic.
5. SQL Server migrations are checked for drift but no real SQL Server authorization, uniqueness, concurrency, bootstrap, or final-owner scenario runs in module CI.
6. BunkFy registers delegable permissions and synchronizes compatibility roles, but it cannot yet enforce that a profile target is an active workspace member or revoke custom profiles when membership stops being active.

## Delivery Slices

### 1. Framework Batch Authorization Primitive

- extend the generic authorization service and decision-provider contracts with bounded batch operations while preserving existing single-requirement behavior and source compatibility;
- preserve provider order, deny precedence, abstain behavior, and deny-by-default independently for every requirement;
- keep the default implementation safe for providers that only implement the single-decision method;
- add Framework tests for mixed allow, deny, abstain, ordering, empty input, and invalid provider result counts.

### 2. AccessControl Batch Decision Path

- make the persisted decision provider batch-aware;
- group requirements by subject and requested scope, query global-role and scoped-profile grants once per group, and apply each permission descriptor's scope policy in memory;
- make profile delegation call the batch authorization service once;
- prove a many-permission delegation check uses one persisted grant query while retaining anti-escalation and explicit allowlisting.

### 3. Product Assignment Policy Seam

- add a narrow Contracts policy interface and immutable context/decision types for scoped-profile assignment eligibility;
- keep built-in actor permission checks and delegation checks inside AccessControl;
- run every registered product policy before creating an assignment and fail closed when any policy rejects it;
- expose only a stable generic rejection error at the AccessControl API boundary while keeping product details out of the module;
- document that membership, staff state, service identity eligibility, and other product rules belong in policy implementations outside AccessControl.

### 4. Scoped Grant Lifecycle Contract

- add a Contracts service that removes all profile assignments for one subject and exact owner scope;
- remove grants and append immutable unassignment history in one AccessControl transaction;
- serialize transactional assignment eligibility, assignment writes, and bulk revocation through the module management lock so an already-authorized assignment cannot commit after lifecycle cleanup;
- make retries idempotent and return the number of removed assignments;
- retain profile definitions and existing history;
- prove active and archived profile assignments are both cleaned up and other subjects/scopes remain untouched.

### 5. GMA Extensions Lifecycle Reaction

- add an opt-in Organizations-to-AccessControl extension;
- react to suspended and removed organization memberships by calling only AccessControl Contracts;
- derive the standard tenant access scope from the organization event scope and record a configured system actor in AccessControl history;
- keep role names, permission sets, invitation behavior, and product UI out of the extension;
- cover active, suspended, removed, replay, and cancellation behavior.

### 6. BunkFy Consumer Alignment

- register a BunkFy assignment policy that permits workspace-profile targets only when their AccessControl compatibility assignment proves active workspace membership;
- call the Contracts revoker from BunkFy's existing membership handler after removing compatibility assignments, preserving one ordered product lifecycle reaction instead of racing an independent generic consumer;
- retain the reusable GMA lifecycle extension for products whose membership policy and composition do not require additional ordered product cleanup;
- verify suspension/removal revokes compatibility roles and every custom profile grant;
- verify cross-workspace or inactive targets cannot receive a custom profile;
- keep BunkFy role names, staff UX, and workspace terminology in BunkFy.

### 7. SQL Server And Canonical Proof

- run representative authorization, duplicate-write, optimistic-concurrency, bootstrap, and final-owner tests against SQL Server as well as PostgreSQL;
- include both migration assemblies in integration-test composition;
- update module CI and the canonical verification script to require both relational providers;
- update Skeleton pins and canonical composition only after Framework, AccessControl, Extensions, and BunkFy verification are green.

## Ownership Boundaries

Framework owns:

- generic single and batch authorization composition;
- provider ordering, deny precedence, abstention, deny-by-default, access subjects/scopes, and permission descriptors.

AccessControl owns:

- persisted compatibility roles, scoped profiles, permissions, assignments, decisions, grant-scope discovery, and security history;
- efficient persisted batch evaluation;
- generic assignment-policy orchestration and scoped assignment revocation contracts;
- provider-backed serialization of transactional access-management commands;
- provider-neutral persistence and front doors.

GMA Extensions owns:

- the optional Organizations membership lifecycle reaction that revokes AccessControl profile assignments.

BunkFy owns:

- workspace membership eligibility policy, role names, delegable permission selection, staff behavior, UI, and product workflows.

No Organizations, Auth, Staff, or BunkFy-specific concept may be introduced into Framework or AccessControl.

## Acceptance Criteria

- authorizing a profile definition performs one authorization batch call and one persisted grant query per subject/scope group;
- batch authorization is semantically identical to independent single authorization, including deny precedence;
- products can reject assignments through Contracts only, and one rejection prevents every AccessControl write;
- a subject's profile grants can be removed for one exact owner scope with immutable history and idempotent replay;
- concurrent assignment and lifecycle revocation are serialized on PostgreSQL and SQL Server;
- organization suspension/removal invokes that operation through GMA Extensions;
- BunkFy rejects inactive and cross-workspace profile targets and removes custom grants when membership becomes inactive;
- PostgreSQL and SQL Server both prove representative security and concurrency behavior;
- Framework, AccessControl, Extensions, Skeleton, and BunkFy retain their dependency boundaries and pass their canonical verification surfaces.

## Explicitly Deferred

- ABAC, ReBAC, deny grants, resource visibility graphs, and row-predicate generation;
- temporary or scheduled access, approval workflows, and bulk import/export;
- profile templates, seed-role UX, invitation-time role selection, and staff-facing product design;
- automatic cascading revocation when a delegating actor later loses authority;
- deletion or legal-erasure policy for security history;
- replacing global compatibility roles with scoped profiles.
