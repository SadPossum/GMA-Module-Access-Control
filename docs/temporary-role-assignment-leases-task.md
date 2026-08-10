# Temporary Role-Assignment Leases Task

Status: complete
Date: 2026-07-28

## Goal

Add a reusable, provider-neutral lifecycle for temporary compatibility-role
assignments. Authorization must stop at the stored expiry even if no cleanup
process runs, revocation must retain bounded review evidence, and products must
be able to reject assignments that violate their own subject, scope, role, or
duration rules.

This is an AccessControl concern. Support tickets, approval chains, customer
communications, incident classification, product role names, and accommodation
scope semantics do not belong in this module.

## Verified Baseline

- AccessControl owns compatibility roles, scoped access profiles, persisted
  decisions, PostgreSQL and SQL Server migrations, and its API/CLI front doors.
- Compatibility-role assignments are currently permanent rows. Revocation
  deletes a row, and authorization does not evaluate time.
- Management writes are serialized by the module management lock.
- Final global-owner removal is protected.
- The existing scoped-profile assignment policy proves the module can expose a
  narrow product-policy contract without importing product concepts.
- Administration already audits the actor, tenant, operation, permission,
  result, error code, and timestamp. It must remain the operation-audit owner.

## Decisions

1. A role-assignment lease is the existing role assignment plus optional
   `ExpiresAtUtc` and `RevokedAtUtc` lifecycle facts.
2. A null expiry remains a standing assignment for source compatibility and
   generic provisioning. Products may reject standing assignments through the
   policy seam.
3. An assignment is active only when it is not revoked and its expiry is later
   than the injected clock. This predicate is applied inside persisted
   authorization and grant-scope queries.
4. Revocation closes the assignment instead of deleting it. A later grant
   creates a new assignment id, preserving bounded lifecycle history.
5. Expiry is enforced by reads and does not depend on a scheduler. Cleanup or
   expiry notifications may be added independently without becoming part of
   the authorization correctness path.
6. Temporary assignment to a role carrying the global owner wildcard is
   rejected. Bootstrap remains the explicit path for the first standing owner.
7. Product policies are conjunctive and fail closed. The generic public error
   does not reveal which private policy rejected an assignment.

## Delivery Slices

### 1. Temporal Assignment Model

- add optional expiry and revocation timestamps;
- remove the historical-row-incompatible unique index and add indexes for
  active subject/scope decisions and lifecycle review;
- index exact role-assignment scopes through a fixed hash while retaining the
  canonical scope predicate for collision-safe equality;
- retain existing rows as standing active assignments;
- create equivalent PostgreSQL and SQL Server migrations.

### 2. Expiry-Aware Authorization

- use the injected clock once per authorization/list operation;
- exclude revoked and expired role assignments in single, batch, and granted
  scope decisions;
- count and protect only active owner assignments;
- keep database filtering and bounded pagination rather than loading assignment
  history into memory.

### 3. Assignment Commands And Policy

- accept an optional UTC expiry in application, API, and CLI assignment flows;
- reject past/now expiries and temporary owner-wildcard assignments;
- add a Contracts-only role-assignment policy context containing the generic
  subject, normalized role name, ordered role-permission snapshot, access
  scope, and optional expiry;
- run all registered policies before persisting the assignment;
- recheck the evaluated role-permission snapshot under the management lock and
  fail closed if the role changed;
- deny role permission expansion while an active temporary assignment exists,
  while allowing privilege-reducing permission removal;
- soft-revoke the exact active assignment and allow a later re-grant.

### 4. Review Surface

- expose expiry, revocation, and a stable lifecycle status in assignment
  responses;
- list active assignments by default and allow an explicit bounded history
  view;
- keep subject ids and scope values out of logs, metrics, notifications, and
  exception messages.

### 5. Proof

- clock-controlled unit tests prove before-expiry allow, at-expiry deny,
  revocation deny, standing compatibility, re-grant, policy rejection,
  permission-snapshot invalidation, and role-expansion freeze;
- PostgreSQL and SQL Server tests prove migration shape, active filtering, and
  concurrent duplicate prevention;
- final-owner tests prove expired or revoked owners cannot satisfy the safety
  check;
- API and CLI contract tests cover expiry input and bounded history output.

## Ownership Boundaries

Framework owns access subjects, scopes, permission composition, clocks, and
authentication-assurance primitives.

AccessControl owns role-assignment persistence, temporal authorization,
lifecycle status, generic policy orchestration, and provider migrations.

Administration owns operation authorization orchestration and durable
actor/tenant/operation audit.

Products own eligible subjects, permitted role names, maximum duration,
resource-scope grammar, and whether standing assignments are allowed.

Private operator systems own requests, reasons, tickets, approvers, customer
visibility, emergency review, and delivery of alerts.

## Done When

- an expired lease cannot authorize even if no worker has run;
- a revoked or expired assignment remains reviewable but cannot block a
  subsequent grant;
- products can deny global, standing, overlong, or otherwise ineligible grants
  without AccessControl knowing product terminology;
- standing bootstrap/provisioning behavior remains source compatible;
- both relational providers and the canonical non-Docker gate pass.

## Local Verification

- boundary checks, warning-free build, migration drift for PostgreSQL and SQL
  Server, the unit suite, and package vulnerability checks pass;
- all 16 Docker-backed relational tests pass across both providers;
- provider upgrade tests backfill the bounded exact-scope hash for existing
  assignments and resolve it through hash plus canonical equality;
- concurrent final-owner tests retain one active owner and one softly revoked
  history row; and
- exact published candidate `fa6b0062a70b85f6f33a9f125417f335cd8f1138`
  passed validate and relational run `31372161631` plus Security Baseline run
  `31372161784`.
