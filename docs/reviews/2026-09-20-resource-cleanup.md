# Resource registration cleanup

The ordinary Add/Build boundary remains intact: configuration registers native factories
during Add; composition later creates the provider and components. No resource registration
callback was moved back into ordinary Build.

## Changes

- Deleted `ApplicationRuntimeResourceSnapshotFactory`, a forwarding object with no distinct
  policy. Revision preparation now performs the same registration and snapshot creation
  directly. The snapshot still outlives component cleanup; rollback and revision metadata
  are unchanged. This is one removed production class, not removal of resource ownership.
- Both resource paths use `ApplicationResourceRegistration.AddProcessingProfile`.
  It validates immutable settings during registration, using System.Text.Json directly
  through JsonElement without an intermediate JSON string. The profile factory is still
  registered in native DI. No new production class or resource registry was introduced.
- Ordinary configuration rejects an invalid processing profile during Add, reports the
  resource address, and preserves the original exception. Failed registration leaves the
  caller's service collection unchanged. Cancellation exceptions remain cancellation.
- Host registrations still override the built-in `processing.profile` registration.

## Verification

- Release solution build: zero warnings/errors.
- Full regression: 2,653 passed, zero failed/skipped, 29 TRX reports.
- Five new cases on each target framework cover application/workflow resource addressing,
  invalid profiles, unknown types, transactional registration and host overrides.
- Reviewed and accepted the public source baseline for `AddProcessingProfile`.
- Fresh eight-package configuration consumer passed on .NET 8 and .NET 10,
  candidate `0.0.0-local.20260920064511`, including both expression engines and resource
  registration/construction counts and cleanup order.
- Evidence: `artifacts/resource-cleanup-20260920/`.

## Remaining boundary

The legacy revision registrar interface, registration context and typed resource contracts
are **not migrated**. They currently provide revision identity, host dependency access,
reference-identity deduplication and recipe change detection. Removing them requires a
behavior-preserving migration, rather than treating them as unused ordinary-build wrappers.
Ordinary configuration does not use them. This step does not claim complete removal of
every historical resource abstraction or a new resource authoring model.

No commit, push, merge or publication was performed.
