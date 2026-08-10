# Access Assignment Policy And Lease Closure Task

Status: complete
Date: 2026-08-10

## Goal

Close the remaining correctness and extensibility gaps around AccessControl
assignment policies and temporary compatibility-role leases. Policy failures
must deny through stable application results, one authorization operation must
use one temporal snapshot, and a lease must still be valid when its serialized
write settles.

This is an AccessControl slice. Framework already owns the generic clock,
authorization, scope, and result primitives needed here. Product membership,
employment, role names, duration limits, approval flows, and operator UX remain
outside the module.

## Verified Baseline

- The current AccessControl head is
  `32b33cb5ac7e971713f85a8e3df118711ee027c9`.
- Exact-head CI run `31016227467` passed both `validate` and `relational`.
- Temporary assignments are soft-revoked, denied at expiry without a worker,
  protected by the provider-backed management lock, and covered by PostgreSQL
  and SQL Server race tests.
- Scope lifecycle exports and destroys standing, expired, and revoked role
  assignments, including expiry and revocation facts.
- Scope closure and assignment growth are serialized by the same management
  lock, and `AccessControlDbContext` rechecks closed ancestors before saving an
  added role assignment.

## Findings

1. `IAccessRoleAssignmentPolicy` and `IAccessProfileAssignmentPolicy` callback
   exceptions currently escape the command pipeline. Persistence remains
   untouched, but callers receive an unstable exception instead of the
   documented fail-closed application result.
2. Both policy contexts copy permissions into arrays exposed as
   `IReadOnlyList<string>`. A policy can cast the value back to an array and
   alter what later conjunctive policies observe, so the contexts are not
   actually immutable.
3. Batch role authorization reads the injected clock once for every persisted
   query. A request split across scope groups or subject batches can therefore
   evaluate one lease on different sides of its expiry within one operation.
4. Assignment input is checked before product policies and lock acquisition,
   but expiry is not rechecked under the management lock. A short lease can be
   persisted as an already-expired successful grant after a slow policy or lock
   wait.
5. Role revocation receives a timestamp captured before management-lock
   acquisition. In a concurrent grant/revoke ordering, that timestamp can
   predate the assignment selected after the lock is acquired.
6. The original temporary-role task still says exact candidate CI is pending,
   although the current head now has exact validate and relational proof. Its
   status should be closed only after this hardening candidate is published and
   verified.

## Decisions

1. Product assignment policies remain conjunctive. `false`, an unexpected
   value represented by a callback failure, or an exception denies the write;
   caller cancellation still propagates.
2. Callback failures are logged only with policy type, operation category, and
   exception type. Subject ids, scopes, role/profile names, permissions, and
   exception messages are not logged.
3. Permission snapshots exposed to policies are defensive read-only copies.
   One policy cannot alter the snapshot observed by another policy or by the
   persistence recheck.
4. One `HasPermissionsAsync` call captures one UTC millisecond instant and
   passes it to every grouped or chunked persistence query.
5. A temporary assignment is revalidated after management-lock acquisition.
   If its expiry is no longer later than the settlement instant, the command
   returns the existing stable expiry error and writes no principal or history
   row.
6. Revocation captures its effective timestamp after management-lock
   acquisition and uses that same instant for active selection and the stored
   lifecycle fact.
7. The existing provider-neutral persistence shape, public policy interfaces,
   admin routes, and product composition stay source compatible. No Framework,
   GMA Extensions, or BunkFy product behavior is added in this slice.

## Delivery Slices

### 1. Policy Boundary Hardening

- contain role and profile assignment policy failures;
- preserve caller cancellation;
- add bounded, redacted failure logging;
- expose permission snapshots through non-cast-mutable collections; and
- prove short-circuit order and stable command errors.

### 2. Temporal Consistency

- pass one captured instant through all grouped batch-authorization queries;
- recheck lease expiry inside the serialized assignment write;
- capture revocation time inside the serialized removal; and
- retain the existing at-expiry deny predicate (`expiry <= now`).

### 3. Provider And Lifecycle Proof

- keep PostgreSQL and SQL Server migrations unchanged and drift-free;
- extend provider tests so an elapsed lease cannot be inserted;
- retain scope export/destruction coverage for lease lifecycle facts; and
- close the original lease task with exact published-commit evidence.

## Done When

- policy callbacks cannot turn an expected denial into an unhandled request
  failure or mutate another policy's permission snapshot;
- one batch authorization operation cannot split its decisions across clock
  instants;
- an elapsed lease returns `AccessControl.AssignmentExpiryInvalid` and leaves no
  assignment history;
- revocation cannot store a timestamp older than the assignment it selected;
- focused tests and the canonical non-Docker gate pass; and
- exact published `validate`, `relational`, and security checks pass before
  downstream pins are advanced.

## Explicitly Deferred

- product-specific standing-assignment or maximum-duration policies;
- approval workflows, scheduled activation, expiry notifications, and cleanup;
- durable assignment-event publication, which belongs in a separate outbox
  slice if a consumer requires it; and
- retention or legal-erasure rules for global security history.

## Local Verification

- `pwsh eng/verify.ps1 -SkipDocker` passes boundary checks, a warning-free
  build, PostgreSQL and SQL Server migration drift checks, all 174 fast tests,
  and the transitive package-vulnerability audit;
- the focused policy and temporal regression set passes all 42 tests; and
- Docker-backed provider tests were not repeated locally. The candidate adds
  equivalent elapsed-lease assertions for both providers, to be exercised by
  exact-head relational CI.

## Published Verification

- exact published candidate `fa6b0062a70b85f6f33a9f125417f335cd8f1138`
  contains functional commit `a9c9f71c55d682569d9bf23430d57a8df4a3c9ff`;
- validate run `31372161631` passed solution synchronization, restore, build,
  boundaries, both migration drift checks, the fast suite, package audit, and
  both providers' Docker-backed relational tests; and
- Security Baseline run `31372161784` passed owned-source, dependency,
  configuration, release-policy, and code-scanning checks.
