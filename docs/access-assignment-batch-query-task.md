# Access Assignment Batch Query Task

Status: complete
Date: 2026-08-10

## Goal

Let a product test whether a subject has any one of several compatibility-role
assignments in an exact scope with one provider query, without teaching Access
Control what those roles mean.

## Ownership

- Access Control Contracts owns the generic facade method and a sequential
  default implementation for backwards-compatible test doubles and adapters.
- Access Control Application normalizes and deduplicates role names.
- Access Control Persistence owns one provider-neutral translated query over
  active exact-scope assignments.
- Products own the role set, authorization decision, and call cadence.

## Invariants

- Empty role sets return `false` without database access.
- Invalid role names fail through the existing role-name guard.
- Matching is by subject kind/id, exact canonical scope, and any normalized
  requested role.
- Revoked and naturally expired assignments never match.
- Local tracked additions remain visible before save, matching the existing
  single-role facade semantics.
- No product names, membership concepts, or permission policy enter the module.

## Verification

- Facade tests cover empty input, normalization/deduplication, and one repository
  call.
- Repository tests cover any-role success, exact-scope isolation, and inactive
  assignment rejection.
- Existing `IAccessControlRoleProvisioner` implementations remain source
  compatible through the interface default.
- No persistence migration or Docker-specific verification is required.

## Completion Evidence

- `eng/verify.ps1 -SkipDocker` passed solution synchronization, boundaries, a
  zero-warning build, both provider migration-drift checks, all 176 unit tests,
  and the transitive vulnerability scan.
- Repository coverage clears the EF change tracker before checking active,
  expired, revoked, and wrong-scope assignments, exercising the translated
  provider query rather than only the tracked-entity shortcut.
- BunkFy's repository gate passed with the product authorizer using the batch
  contract for owner, current membership-marker, and legacy-member roles.
