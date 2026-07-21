# Access Profile Management Contracts Task

Status: implemented
Date: 2026-07-22

## Goal

Let composing products expose product-shaped role management without referencing AccessControl Application code or routing their own server-side workflows through HTTP.

## Boundary

- AccessControl owns profile persistence, validation, permission eligibility, actor anti-escalation, optimistic concurrency, archival, and immutable history.
- Products own profile names, descriptions, presets, permission presentation, assignment eligibility, resource scopes, and user experience.
- The Contracts facade contains no product role names, organization concepts, property semantics, or UI metadata.

## Delivery

- Add `IAccessProfileManager` for paged profile reads, allowed-permission inspection, profile detail, create, update, and archive.
- Enforce `access-control.profiles.read` or `access-control.profiles.manage` inside the facade before dispatch, then reuse the existing CQRS commands and queries so API and Contracts callers share validation, delegation, locking, and persistence paths.
- Return framework `Result` values so expected validation, delegation, not-found, and concurrency failures are preserved for the composing product.
- Keep assignment reconciliation in the existing provisioning facades; management does not add a second assignment model.

## Verification

- contract reads map only stable Contracts DTOs and retain page metadata;
- mutations preserve actor identity, expected versions, and application failures;
- denied actors fail before a query or command is dispatched;
- global owner scopes and empty profile ids fail before dispatch;
- DI registration is idempotent;
- architecture, build, migration drift, unit, package, and relational-provider gates remain green.
