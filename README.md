# GMA AccessControl Module

This repository owns the optional GMA AccessControl module: persisted compatibility roles, scoped access profiles, subject assignments, security-policy history, and the decision provider used by `Gma.Framework.AccessControl`.

It is consumed by source-first applications and by the `GMA-Skeleton` composition repository under `gma/modules/access-control`.

Useful entry points:

- `Gma.Modules.AccessControl.slnx`
- `docs/README.md`
- `eng/verify.ps1`

Consumers integrate through `Gma.Modules.AccessControl.Contracts`. Persistence ports in Application are module-internal seams and are not consumer APIs.

The Contracts package includes profile provisioning and exact-set assignment reconciliation for product composition. The module owns generic concurrency, delegation, assignment-policy, and history mechanics; products retain their profile names, permission selections, defaults, and lifecycle policy.

Hosts may configure `AccessControlApiSecurityOptions.ProfileManagementAssurance`
with a GMA `AuthenticationAssuranceRequirement`. When configured, access-profile
and assignment mutations require that assurance; read endpoints retain their
existing behavior.
