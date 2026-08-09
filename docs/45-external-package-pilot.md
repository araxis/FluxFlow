# External Package Pilot

FluxFlow has been validated through a standalone consumer application at
`C:\Projects\FluxFlow.Pilot`. The pilot is outside this repository and has no
project reference to FluxFlow source.

## Purpose

The repository acceptance fixture proves release mechanics. The external pilot
adds a developer-adoption check: can a small application understand, configure,
run, reload, observe, stop, and recover FluxFlow using packages alone?

The pilot intentionally uses the public surface without helper code from this
repository.

## Scenarios

### Typed code-first

The pilot declares one complete uppercase component contract, adds two typed
instances to one workflow, connects typed output to typed input with a C#
predicate, builds the definition, and registers it once with
`AddFluxFlow(definition)`.

It sends and receives through typed handles, verifies the exact transformed
value, queries the standard readiness check for the active revision, and stops
the application cleanly. It does not repeat ordinary component registration.

### Portable JSON

The pilot loads its active and invalid definitions from JSON files. Because
portable JSON contains names rather than executable factories, this path
registers its component catalog explicitly.

Execution proves:

- initial addressed routing;
- same-definition `Unchanged`;
- independently loaded invalid candidate `Rejected`;
- exact active revision and definition retention;
- successful routing after rejection;
- clean stop.

### Restart durability

One process persists a durable input through the SQL-file provider, acquires a
lease, and exits without settling it. A separate recovery process uses the same
absolute data directory and a deterministic later clock.

The recovery host observes the expired lease, executes the embedded-contract
workflow, captures the typed output durably, delivers it to an idempotent local
destination, and reaches exact terminal state: one delivered input, one
completed output, and one effect.

## Package isolation

The verification runner restores these nine exact packages from the public
package feed only:

- `FluxFlow.Nodes` 4.0.0
- `FluxFlow.Mapping` 1.0.3
- `FluxFlow.Composition` 7.0.0-rc.1
- `FluxFlow.Engine` 8.0.0-rc.1
- `FluxFlow.Engine.HealthChecks` 1.0.0-rc.1
- `FluxFlow.Engine.DurableInput` 2.0.0-rc.1
- `FluxFlow.Engine.DurableInput.SqlFile` 2.0.0-rc.1
- `FluxFlow.Engine.DurableOutput` 4.0.0-rc.1
- `FluxFlow.Engine.DurableOutput.SqlFile` 4.0.0-rc.1

Restore uses one pilot-owned package cache. The runner verifies the exact graph,
package asset type, and public-source metadata for every resolved FluxFlow
library. The application project has package references only; the test
project's one project reference remains entirely inside the pilot.

## Verification result

- FluxFlow publication commit:
  `d6c245df82fb2958a77cff04985811fb49f12b04`
- External pilot commit: `9e5699b`.
- All nine exact packages reported the public package feed as their source.
- Build: 0 warnings and 0 errors.
- Tests: 6 passed, 0 failed, 0 skipped.
- Formatting verification passed without changes.
- Exact outputs:
  `PILOT CODE FIRST`, `PILOT JSON`, `PILOT STILL ACTIVE`, and
  `PILOT DURABLE RESTART`.
- Overall marker: `PILOT_VERIFICATION_OK=True`.
- The isolated cache and restart state were removed by the normal runner.

The pilot found no blocking public-surface or operational usability defect. No
FluxFlow production source, public API, JSON format, package version, or
dependency changed during this work.
