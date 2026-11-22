# Application API organization

This pass completes item 7 of the scoped Application review. The code already used
native DI and Dataflow, but extension methods were grouped under misleading names.
ComponentRegistrationExtensions included application building and resource registration;
FlowExtensions hid application operations behind an unspecific name. Its private
FindPort helper also resolved components, making that name inaccurate.

FlowApplicationExtensions replaces FlowExtensions and groups AddFlowWorkflow,
BuildApplicationAsync, Link and GetComponent. AddApplicationResources is moved into
ResourceRegistrationExtensions. ComponentRegistrationExtensions now contains only
component factories, ports and their private registration helpers. No class, wrapper,
lookup or alternate execution path is added. The number of production types remains
unchanged. The private lookup helper is named GetRequiredService.

FlowApplication's private MakeLinks method is renamed DeclareLinks. It executes link
declaration delegates during Build; AttachLinks performs native LinkTo calls during
Start. LinkFactory's documentation now describes that distinction. Delegate names,
arguments and behavior remain unchanged, as do lifecycle synchronization, disposal
ownership and registration validation. Unused hosted-service imports are removed.

Extension calls keep their names, arguments and namespace. This is nevertheless a
development-preview API migration: explicit static calls and using-static directives
must reference the new declaring classes, and dependent binaries must be rebuilt.
There are no compatibility aliases. The source public API baseline changes only the
Application hash; its 88 recorded declarations are retained.

Existing tests and the isolated package consumer exercise the same registration,
build, resource and link behavior. Tests referring directly to FlowExtensions now
refer to FlowApplicationExtensions; no implementation-mirroring tests are added.

Validation: all 2,099 Release test executions pass on .NET 8/10 without warnings.
All 32 Debug projects build with zero warnings/errors. The isolated Application
package consumer passes on both frameworks, and the reviewed public API baseline
passes. `git diff --check` passes. No commit or publication in this step.

Local evidence: `%TEMP%/flux-application-api-organization` and
`%TEMP%/fluxflow-application-consumer-3c0758697aa64d9b9302050e8eb45ced`.

## Cleanup closeout

A subsequent caller/dependency audit found no unreferenced production class or
parallel implementation in the Application project. Its 17 C# files retain these roles:

| Files | Responsibility |
| --- | --- |
| FlowApplication, FlowWorkflow | Application lifetime and workflow grouping |
| Four registration/access extension files | Component ports, application operations and resource registration/resolution |
| FlowFactories | Existing component and deferred-link delegate contracts |
| Two hosting files | Standard host startup, shutdown and completion observation |
| ComponentEventBridge | Address enrichment and event forwarding cleanup |
| Four Addressing files | Shared address syntax and typed component/port values |
| Three Execution files | Link declarations, runtime completion/failure coordination and cycle detection |

Deleting these files would currently require removing behavior or moving unrelated
responsibilities together. No further production restructuring was justified by this
audit. The remaining cleanup is in test/dependency residue:

- Removed unused NodePorts compilation from node tests and benchmarks, and unused
  ScalarValue compilation from benchmarks. The shared source files remain for their
  actual test consumers.
- Routing's Gate test resolves input ports through native keyed DI, so it no longer
  compiles NodePorts or needs Application internals.
- Application now grants internal access only to FluxFlow.Core.Tests. Removed friend
  permissions for node tests, routing tests and benchmarks; those projects compile
  against the public Application API.
- Consolidated configuration-access and reload-boundary checks in
  ApplicationBoundaryTests and deleted LinkOwnershipBoundaryTests. Removed a duplicate
  address-assembly check and checks for old Composition type names that no longer
  describe the current architecture. Runtime behavior tests remain intact.
- Updated FlowLink's summary and removed unused imports. Public API is unchanged.

This closes the scoped cleanup; it is not a claim that future requirements or reviews
cannot reveal further improvements. No new report file or production type was added.

Cleanup validation: 2,094 Release test executions pass without warnings. The decrease
from 2,099 is the removed/consolidated obsolete checks, not skipped or failing tests.
All 32 Debug projects build without warnings/errors; the public API baseline remains
unchanged. The isolated Application consumer passes on .NET 8/10. Diff whitespace
checks pass. No commit or publication.

Cleanup evidence: `%TEMP%/flux-application-cleanup` and
`%TEMP%/fluxflow-application-consumer-cc394fdacfb44cadb61bda1cf6292898`.
