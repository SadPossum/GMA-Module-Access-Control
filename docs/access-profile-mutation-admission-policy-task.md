# Access-Profile Mutation Admission Policy

Status: completed
Date: 2026-07-31

## Goal

Provide a product-neutral application seam that lets a composed host admit or
reject access-profile mutations before Access Control changes owned state.

The module must not know about tenant termination, billing, employment, or any
other product workflow.

## Scope

- Add a contracts-only policy, context, decision, and stable operation names.
- Cover profile create, update, archive, and non-idempotent ensure.
- Keep exact ensure replays available because they do not mutate state.
- Preserve current behavior when a host registers no policy.
- Contain policy failures and distinguish rejection from unavailable policy
  state.
- Keep profile assignment and role assignment on their existing dedicated
  policy seams.
- Keep assignment removal and profile reads outside this mutation policy.

The context carries only the operation, profile owner scope, actor, and
optional profile id or key. Product-specific lifecycle vocabulary and policy
state belong in the composing application.

## Acceptance

- every covered mutation is admitted before its first write;
- denied or unavailable decisions leave the profile unchanged;
- one command evaluates mutation admission at most once;
- an existing ensure replay bypasses admission;
- enum wire names reject numeric, unknown, and future values; and
- focused tests and the repository non-Docker gate pass.

## Verification

- the standalone non-Docker module verification gate passed with architecture
  boundary checks, a zero-warning build, and both provider migration-drift
  checks;
- all 132 unit tests passed;
- the package audit found no vulnerable packages; and
- no persistence migration is required because this slice adds no owned state.
