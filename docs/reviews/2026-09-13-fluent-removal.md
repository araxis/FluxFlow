# Retire the Fluent construction layer

The user rejected both the FlowGraph wrapper and the second vocabulary of Then,
Branch, Tap and reusable FlowSegment builders. The direct Core application already
owns the graph and provides flexible native endpoint links.

Removed the FluxFlow.Fluent source package and its builder-specific test project.
No replacement builder, compatibility facade or runtime was introduced. Direct
Core's internal topology snapshot remains; it is execution state, not another
public graph API. Portable definition builders and JSON were not redesigned.

Preserved independent capabilities:

- FluxFlow.Hosting replaces FluxFlow.Fluent.Hosting. AddFlowApplication accepts a
  direct application factory with DI access and retains startup, stop and disposal.
- FlowEventStream in the existing optional FluxFlow.Observability package observes
  explicitly selected component event sources. Native BroadcastBlock/ActionBlock
  delivery remains best-effort, bounded for callbacks, and isolates callback errors.
  The stream owns only observation links, not the application or its components.
  Disposing it releases the completion wait without waiting for sources to stop.
- FluxFlow.DirectSample replaces FluentSample, showing explicit linear links,
  parity branches and fan-in. Its run produced ALPHA/BETA/GAMMA and all six labels.
- The package consumer fixture now constructs its direct scenario using Core.
  Removed the Fluent package/version input; renamed its success marker to DIRECT.
  A temporary source-reference build of the fixture compiled and ran all normal
  scenarios successfully. This is source integration, not fresh NuGet acceptance.

Transferred five host tests (now both net8/net10) and five event/error tests.
Added two observer lifetime/completion tests. Builder-specific syntax tests were
removed with the API; Core's graph suite and the direct branching sample retain
execution coverage. Observability tests now share one xUnit collection because
their process-wide DiagnosticListener would otherwise see each other's exceptions.

Validation: Core 150 per framework, hosting 5 per framework, observability 16 per
framework passed. Normal API snapshot verification passed 2 after reviewed removals
and new independent adapter APIs. Full solution Release build passed with zero
warnings/errors. Release conventions initially found a required null manifest
baseline and obsolete metadata/assertions, subsequently corrected. All 167 final
release tests passed; report: artifacts/fluent-removal/release-final.trx.

Migration details are in docs/46-code-first-major-migration.md. Existing historical
reviews retain their original descriptions. No release, publication, external UI
migration or backend deployment was performed. Next: return to review/design of
portable JSON definitions using the direct C# model; Sessions remains deferred.
