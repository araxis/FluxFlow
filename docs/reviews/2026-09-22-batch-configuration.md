# Batch configuration adapter

Added two production files in FluxFlow.Configuration/Batching:
BatchConfiguration and BatchRegistrationExtensions. AddBatch registers the existing
section callback contract under `batch`. The callback binds its own DTO, validates
settings, snapshots them and registers a deferred native component factory and
Input/Output/named Events ports. No new builder, definition model or context.

BatchSize and MaxWait must be positive; MaxWait uses the existing Batch timer limit.
OutputCapacity defaults to 128. The factory resolves TimeProvider from the host or
uses TimeProvider.System. Native ordered execution defaults are preserved.
No changes to Core or the Batch component were required.

Configuration gains a Batch project/package dependency. The isolated package consumer
now includes Batch after Router and verifies its original mapped message is retained.
Public API baseline and current setup guides are updated.

## Validation

- Configuration: 142 tests per target on .NET 8/10, all passing.
- Eleven new cases cover invalid/omitted settings, registration atomicity, lazy
  dependencies, input/output identity, named event resolution, settings snapshots,
  size/time/completion flushing, original envelopes and pending state across Build.
- Full solution run passed all component/runtime tests. One release convention found
  the performance-baseline index link omitted by the preceding documentation cleanup.
  Restored that link; all 103 release tests then passed. Final suite coverage totals
  1,818 cases (full run plus corrected release rerun).
- Logs: artifacts/cleanup-review-20260920/batch-configuration-tests.log,
  batch-full-tests.log, batch-release-tests.log and batch-api.log.
- Isolated NuGet restore and the Scheduler → Source → Gate → Mapper → Router → Batch
  consumer passed on .NET 8/10. Evidence: batch-package-final.log in the same log
  directory. The package check includes the new Batch dependency. Whitespace checks pass.

Next design review: ChangeFilter/Deduplicate key selection and equality for configured
expressions. Their generic CLR key contracts need a deliberate configuration choice.
No commit/publication.
