# Engine Canonical Runtime Simplification

Date: 2026-07-25

## Status

The internal Engine assembly and stable-port simplification is complete on
local branch `work/canonical-vnext-cleanup`. No push, tag, package publication,
pull request, or merge was performed.

## Runtime Assembly

- `ApplicationRuntimeAssembler` is now a small lifecycle facade responsible
  for serialized preparation, initial revision-event buffering, stable port
  generation adoption, current-port publication, and disposal.
- Link compilation and surface planning moved to
  `ApplicationRuntimePlanFactory`.
- Reflection-free stable port discovery and runtime construction moved to
  `ApplicationRuntimePortSurfaceFactory`.
- Workflow keyed-service views and revision input/output binding moved to
  `ApplicationRuntimePortBinder`.
- Candidate construction and attempt-all preparation rollback moved to
  `ApplicationRuntimePreparation`.
- Stable runtime ownership and reference-counted retirement moved to
  `ApplicationRuntimePortGeneration`.
- Workflow snapshots are appended directly to the preparation-owned snapshot
  collection, so partial workflow snapshot construction remains visible to
  rollback.

## Port Runtime

- Message and payload-independent signal inputs now share one internal
  attachment/revision lifetime implementation. Their distinct message and
  signal delivery behavior remains in their existing typed port cores.
- Rejection, input/output activity, request timing, diagnostic construction,
  and system-event publication moved from `ApplicationPortRuntime` to
  `ApplicationPortEventPublisher`.
- Output broadcast ownership, input mailbox behavior, revision routing,
  buffering, cancellation, completion, and retirement remain separate where
  their semantics differ.
- A focused regression proves message and signal attachments retire
  idempotently and can be replaced without retaining the previous target.

## API And Compatibility

- No public Engine declaration or package version changed; Engine remains
  `3.0.0` from the preceding legacy-runtime removal.
- The lightweight source-declaration baseline count for manifest index 7
  changed from 342 to 336 because duplicated public-looking members inside
  internal/private implementations were consolidated and moved. This is not a
  package API change.
- SDK package validation of the current Engine `3.0.0` package against a
  temporary `3.0.0` package built from pre-refactor commit `93e8b10` passed.
- SDK validation against published `2.7.1` continues to report only the
  already reviewed CP0001 next-major removals. No compatibility suppressions
  were added.

## Verification

- Engine: 55 passed, zero warnings.
- Composition: 107 passed, zero warnings.
- Composition Hosting: 29 passed, zero warnings.
- Release: 97 passed, zero warnings.
- Controlled Debug and Release builds each completed 131 projects with zero
  warnings and zero errors. The first cold invocations exceeded the command
  window but completed; immediate controlled reruns supplied the authoritative
  successful results.
- Engine `3.0.0` release preflight passed.
- A fresh temporary source outside the repository was seeded with all 58
  current packages. Engine archive, package smoke, feed, and fast dry-run
  checks passed against that source.
- A temporary net8.0 consumer with 58 direct current-package references
  restored from the complete source and built in Release with warnings treated
  as errors.

## Remaining Program Work

The next bounded phase is removal of obsolete Control When/Filter and Routing
Switch/Fork/Merge structural compatibility after canonical conditional-link,
fan-in, fan-out, default-route, and cross-workflow parity is proven. Remaining
family audits, including MQTT compatibility cleanup, and the final
documentation/package completion audit follow separately.
