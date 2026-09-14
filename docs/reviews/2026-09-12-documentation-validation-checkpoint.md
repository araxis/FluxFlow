# Documentation and coordinated validation checkpoint

## Documentation changes

- Root README uses the real FlowValueTransformNode example and canonical FlowMessage
  transport, identifies FluxFlow.Core as the consolidated runtime package, and links
  the new current component-development guide.
- docs/45-component-development.md explains value-only and envelope-aware processing,
  local parsing, domain errors, optional Composition, metadata factories and examples.
- Composition's README describes canonical physical ports. MQTT Composition's README
  distinguishes successful result payloads from FlowError messages and corrects the
  physical port descriptions.
- Documents 19, 22 and 40 are explicitly marked historical and linked to the current
  guide. Their intermediate migration instructions remain preserved as history.
- Valid typed definition/Engine helper examples and removed-type migration notes
  remain intact. No source-code examples required implementation changes.

## Coordinated regression results

All runs used Release with --no-restore on this checkout. All 20 selected test
projects passed: 1,303 cases summed across projects, 1,802 executions including
multi-target runs. Nodes, Core and Metrics ran on net8.0 and net10.0; the remaining
selected test projects target net10.0. No failures or skips were reported.

| Test project | Cases | Executions |
| --- | ---: | ---: |
| Nodes | 302 | 604 |
| Core | 144 | 288 |
| Composition | 273 | 273 |
| Fluent | 39 | 39 |
| ComponentPackageTemplate | 9 | 9 |
| Storage / Composition | 45 / 2 | 47 |
| FileSystem | 55 | 55 |
| Sessions | 53 | 53 |
| Metrics | 53 | 106 |
| Projections | 27 | 27 |
| Expectations / Composition | 20 / 11 | 31 |
| State | 19 | 19 |
| HTTP / Composition | 36 / 8 | 44 |
| MQTT / Composition | 139 / 35 | 174 |
| Serialization / Composition | 17 / 16 | 33 |

The ten affected component Composition projects built successfully, including their
runtime dependencies and net8.0/net10.0 targets, with zero warnings/errors. All eight
sample projects also built with zero warnings/errors. FluentSample ran successfully
(ALPHA/BETA/GAMMA and all six even/odd labels); CompositionSample ran successfully
(ALPHA/BETA). Server samples were built but not started.

Local raw logs are in the temporary directories:

- C:/Users/meisa/AppData/Local/Temp/fluxflow-validation-cafd070fd0b0489b8b45ec027ef3095a
- C:/Users/meisa/AppData/Local/Temp/fluxflow-build-c67dbb06c601499ebde64ff8e265f003

## Scope and next decision

This validates the refactored packages together; it is not a full solution/release
qualification. Engine-wide, durability-provider, external UI/package-feed and live
service integration suites were not rerun. No packages were published and no
performance benchmark was run. Existing tests do not prove complete OpenTelemetry
support, which remains planned work.

The current implementation/documentation checkpoint is complete. Phase 1 is not
automatically declared complete by these results. The next decision is whether the
agreed simplification scope is sufficient to begin phase 2, or whether another
specific development pain point warrants a bounded change. Avoid inventing further
refactors solely to continue reducing line count.
