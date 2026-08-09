# External Package Pilot

Date: 2026-08-08

A standalone consumer was created at `C:\Projects\FluxFlow.Pilot` to validate
FluxFlow outside the repository. It contains no project reference to FluxFlow
source and restores exact locally packed candidate bytes.

## Boundaries proved

- Normal typed C# authoring uses complete contracts, typed handles, and one
  `AddFluxFlow(definition)` registration without repeated component setup.
- Portable JSON remains explicit: files contain portable names and the host
  registers the corresponding catalog deliberately.
- The JSON path preserves the active route across unchanged and rejected
  updates.
- Standard readiness reports the exact active revision.
- SQL-file durability survives a real process boundary: an abandoned input
  lease is recovered, the workflow runs, output is captured and delivered, and
  both stores reach exact terminal state.
- No reflection, scanning, dynamic activation, external infrastructure, or new
  FluxFlow dependency was introduced.

## Evidence

- Source commit: `2756c32571319463fa851171d9436c2de2a80dd1`.
- Nine exact candidate packages and nine matching restore hashes.
- Warning-free build and clean formatting verification.
- Five focused xUnit/Shouldly tests passed with no failures or skips.
- Exact code-first, health, JSON, durability seed/recovery, and overall markers.
- Candidate feed, isolated cache, and restart state removed after verification.

The pilot found no blocking adoption defect. Publication remains a separate
decision.
