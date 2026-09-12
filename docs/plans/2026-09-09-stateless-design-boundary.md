# HTTP, mapping and validation design boundary

Status: implementation in progress within the approved full core simplification.

| Package family | Disposition | Targets and dependencies |
|---|---|---|
| Http / Mapping / Validation runtime | Retain all execution, materialization, resource ownership and diagnostics | Existing net8.0/net10.0 projects and references |
| Each family Composition | Retain structural descriptors and trusted portable bindings; remove presentation reference | Runtime family plus Composition; net8.0/net10.0 |
| Each family Designer | New optional descriptor projection, with existing hints and defaults | Own Composition plus Components.Designer; net8.0/net10.0; 1.0.0-rc.1 |

Use AddHttp, AddMapping or AddValidation for registration. Visual consumers opt in
with AddHttpDesign, AddMappingDesign or AddValidationDesign afterwards. Direct
constructors and instance-first graphs are unchanged. No new creator, runtime
owner, service locator or general presentation builder is introduced.

Preserve processing capabilities, canonical input shapes, ports, required options,
resources and configuration binding. Keep all node/client/evaluator/schema code
unchanged. Optional metadata must not activate nodes or resolve host resources.
Preserve editor labels, groups, ordering, defaults, bounds and resource hints.

Update existing visual test fixtures and the designer sample explicitly, while
leaving runtime fixture registrations independent. Add boundary, nonactivation,
idempotence and invalid-order tests for each family. Keep actual request, mapping
and schema execution tests. Review source API rows individually; update manifest,
destination map and solution before qualification. No existing package version bump.

Verification: focused tests, complete non-incremental Release build, selected
solution regression, workflow acceptance, new-test discovery, six local archives
and exact dependency-group inspection. Local packs are not a coherent candidate
feed or isolated consumer acceptance. Performance and the wider architecture gates
remain open. This batch reduces remaining ordinary Designer references from 17
to 14; it does not complete the full goal or authorize publishing.
