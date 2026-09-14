# Builtin context registration completed — step 76

All 38 remaining builtin registrations now construct their nodes and declare actual
ports on ComponentActivationContext. All 45 builtin registrations across 19
families use this path. DI registration and typed C# definitions share the same
factory callback. No builtin ComponentContract, CreateBound, CreateStandard,
CreateNative or ComponentNodeActivation usage remains under src/Components.

## What changed

Removed the remaining 15 static contract containers. Retained all 44 typed AddX
methods and their 55 capture overloads, existing configuration and typed handles.
Sources register only their real outputs/events. MQTT Ack/Nak retain signal
semantics; Retry control inputs retain message semantics. Native Dataflow blocks
and runtime processing algorithms are preserved.

Sessions and Storage keep asynchronous acquisition, shared versus owned resource
behavior, and cleanup on constructor/build failure. Owned cleanup is attached
through context.SetLifetime. HTTP declares its existing parallel relaxed-order
processing capability while unsupported profiles remain rejected. Processing
validation now occurs in the direct factory context and still applies when a
factory does not bind configuration.

One intermittent Gate test subscribed after sending to a documented live output,
which could discard the message before subscription. The test now subscribes
before sending; no production delivery behavior or timeout was changed.

This simplifies the registration structure rather than claiming a line-count
reduction. Across the 34 production C# files captured immediately before this
step, lines changed from 3,478 to 3,622 (144 net added). Explicit port declarations
and retained typed convenience methods account for the new shape. This scoped
count excludes tests, documentation and other inherited working-tree changes.

## Verification

- Release build: zero warnings and errors.
- Final configured local regression: 4,688 passed, zero failed, 76 TRX reports.
- New ownership coverage: 29 cases, including seven async factory types,
  owned/shared resources, failed builds and constructor failure cleanup.
- HTTP profile coverage verifies actual concurrent execution and rejection of
  unsupported preserved-order parallel processing.
- Public API baseline reviewed and its normal verification passed.
- Fresh local candidate: 0.0.0-local.20260914.context.1; 63 NuGet packages.
- Independent package-only consumer passed on net8.0 and net10.0: all 45
  factories registered and a typed Serialization roundtrip executed. Its assets
  contain 43 FluxFlow package dependencies at the candidate version and zero
  project references.
- Existing comprehensive package-only acceptance passed on net8.0, including
  Engine JSON/C#, resource/health behavior and SQLFile durability/restart checks.

Evidence: artifacts/all-context-migration-20260914/verification.json,
build-final.log, regression-final/, pack.log, consumer-net8.log,
consumer-net10.log and acceptance.log. Package hashes are in verification.json.
Focused runs overlap the final count and must not be added to it.
The main local regression excludes Acceptance, ContainerIntegration, ExternalSmoke
and TSql.IntegrationTests. Separate package acceptance is stated above; no real
external-service conformance was run. The package build emitted the expected
nonpackable HttpTrigger sample warning.

## Remaining boundary

Legacy custom-template APIs still exist in Composition and custom samples/tests.
Builtin migration is complete; global removal of ComponentContract and its helper
APIs is not. The next cleanup is to audit those remaining custom usages, migrate
needed examples to context registration, and remove obsolete APIs while preserving
actual supported behavior. This is separate from the completed 38-item migration.

No publication, push, external consumer adoption or changes to FluxFlow.UI occurred.
