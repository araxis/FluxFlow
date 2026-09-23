
Latest output-policy cleanup: FlowWorkflow.DiscardedOutputs and the ordinary
DiscardUnmatched API/DI callback are removed, together with JSON policy parsing,
definition-handle declarations and revision discard-set propagation. No replacement
lookup or automatic sink is added to production. Unmatched messages retain the
existing routing error; build-time output validation and application error collection
are deferred. Low-level ApplicationPortRuntimeBuilder's output-drain option and
system/error-isolation drain behavior remain outside this workflow-property removal.
Tests use explicitly registered terminal consumers rather than workflow discard flags.

Validation: 2,618 solution tests passed (including 505 Core cases per target and
494 Engine cases). Release build: zero warnings/errors. API baseline updated;
the revised guide 39 consumer compiled and started/stopped successfully. Retired
policy-only tests account for the reduction from 2,660; routing, fault, backpressure
and revision regression suites still run. Evidence is under
artifacts/cleanup-review-20260920/discard-*.log. No commit or publication.

# Pre-setup cleanup review

Latest application file consolidation: FlowApplication.Composition.cs is removed and
FlowApplication is no longer partial. Its construction methods live directly in
FlowApplication.cs before execution initialization. No replacement class or extension
was added. Component-address parsing is reused in the port filters. This is an
organization change, not a new lifecycle design. Validation: 526 Core tests on each
target plus 494 runtime/revision tests passed; API baseline and whitespace checks passed.

Reviewed the native keyed-DI working tree before extending component setup. This is
a bounded review, not a claim that all remaining code is unnecessary.

## Confirmed behavior requiring cleanup

1. `ComponentRegistrationExtensions.BuildApplicationAsync` silently constructs an
   application if none is registered. With an empty service collection, two Build
   calls return different instances, neither owned by DI. Require the registered
   application rather than restoring a parallel ownership path. Empty configuration
   itself was rejected by configuration validation; this finding concerns the public
   Build fallback, not an accepted empty-configuration scenario.
2. The same method defaults `linkCapacity` to 128 and passes it to composition even
   when the registered application was constructed with 17. The probe observed 128
   after Build; a subsequent Build with 17 failed. Establish one consistent source
   of capacity settings, preserving explicit configuration and repeated Build.

The standalone probe is in ignored `artifacts/cleanup-review-20260920`. Both behaviors
were reproduced on .NET 10. Follow-up: both findings are now fixed. Build requires the
registered application; the no-capacity overload uses its configured capacity. The
explicit int overload remains. Three regression tests cover missing registration,
constructor capacity, and repeated Build after an explicit capacity override.

## Documentation and internal cleanup

- README and current hosting/authoring guides still describe removed scopes,
  application-owned component disposal, FlowExecution, activation contexts and old
  package APIs. `FlowFactories.cs` also describes a temporary non-owning provider,
  although LinkFactory now receives the real provider. Correct current guidance;
  preserve historical reports as history.
- `DocumentationBoundaryTests` requires obsolete Engine wording in the opening of
  the hosting guide. Update those expectations alongside the documentation.
- Revision `ProviderSnapshots` materializes metadata whose only production consumer
  checks that it is non-null. Remove this redundant contract if its diagnostic purpose
  is not needed. Preserve the actual candidate provider, rollback and disposal.
- `DisposeComponentsAsync` no longer disposes components; rename it to describe
  its application cleanup responsibility.
- A keyed ComponentFactory is stored in addition to the canonical keyed IFlowNode;
  production checks its registration but invokes the captured factory through IFlowNode.
  Consider removing the duplicate service registration after checking compatibility.
  The ComponentFactory delegate itself remains useful and should stay.

## Addressing correction completed during review

- ApplicationAddress now creates a child port through `Port(name)`.
- Native input/output/named-event registrations and duplicate checks use its Value.
- Configuration and composition construct component keys with WorkflowComponent(...).Value.
- No new registry, wrapper, catalog or provider was introduced.

The identity guard lock remains intentionally: it executes when factories resolve,
not while adding service descriptors. Different keys/providers can resolve concurrently.
The check-and-add must be atomic to reject the same component object before DI captures
it twice. Synchronous methods do not imply single-threaded invocation.

## Validation and next step

Core: 532 tests per target; Configuration: 73 per target, on net8.0 and net10.0.
Total for this change: 1,210 passed, zero failed or skipped.

Follow-up also refreshed README, Core package guidance, factory XML and ordinary-first
hosting documentation, with matching documentation-test expectations and API baseline.
The longer authoring guide and internal metadata cleanup remain outstanding.

Final follow-up validation: all solution tests passed (2,681 across 29 target runs),
including 497 revision/runtime tests. Empty revisions now explicitly register their
application in the candidate provider instead of relying on Build fallback.
Evidence: `artifacts/cleanup-review-20260920/solution-tests-final.log`.
`git diff --check` passed. No commit or publication was performed.

Provider metadata follow-up: removed CompositionProviderSnapshotInfo, the candidate's
ProviderSnapshots projection/contract, and its null-only validation. Snapshot creation
no longer collects creation time, boundary or service count as instance metadata.
The actual provider and its ownership flag remain to preserve disposal/rollback.
Removed the test that only serialized the deleted metadata record; ownership,
concurrency, rollback and lifecycle tests remain. Renamed DisposeComponentsAsync to
DisposeAfterDrainAsync to describe application cleanup without implying DI disposal.

Input-side string/array connections are covered by InputPortConnectionTests;
output-side arrays and conditions by ApplicationLinkCompilerTests. Both declaration
directions remain supported. ComponentPortMetadata and its Single restriction are
still present: their removal is the next separate migration across link compilation,
registration and the revision port surface. Do not report all metadata removed.

Provider-metadata validation: solution behavioral tests passed, including 496 runtime
tests. The declaration-baseline check initially detected the intentional removal of
the internal record's public properties; the baseline was updated and its tests passed.
The removed serialization-only test accounts for the count decreasing by one.
Evidence: metadata-tests.log and metadata-release-final.log in the same artifact directory.

Cardinality increment: removed ComponentPortLinkCardinality, registration/handle options,
exclusive-claim validation and its diagnostic. Existing tests were changed to verify
multiple destinations, shared inputs and mixed input/output declarations. Duplicate
links, invalid conditions, fallback conflicts and cycle checks remain. Five Core test
cases per target that tested only removed metadata-cardinality mechanics were removed;
behavioral multi-link tests remain and pass. The metadata record now retains only the
name/type/signal-kind consumed by the older compiler and revision path.

Port-registration increment: ComponentPortMetadata, ComponentPorts and ComponentPortKind
are physically deleted. DI discovery contains addresses and the explicit signal option;
it no longer duplicates names and message types in metadata records. The compiler uses
canonical FlowValue types for runtime ports and ordinary Type values for its legacy
typed validation. Typed test fixtures remain test-only and are never registered in DI.
Revision port surfaces retain only their address, direction and signal behavior.
The two tests that tried to inject a noncanonical metadata type into a revision surface
were removed because the surface no longer accepts a type. Native identity/message
transfer and signal tests remain. Four metadata-only tests per Core target were removed.

Remaining: ApplicationSystemOutputMetadata and ApplicationPortMetadata in the older
system-output/stable revision API, plus stale authoring documentation. This increment
does not claim their removal.

Subsequent runtime increment COMPLETED: both records above are now removed, together
with ApplicationPorts.Metadata and the duplicated metadata-by-address table. Existing
Status.Ports reads each actual port's state and preserves address/direction/kind/type/
capacity inspection. Rejection diagnostics query actual input membership. System-output
validation accepts ordinary address/type pairs; no new custom record replaces it.
MissingSystemOutputType preserves the former diagnostic's numeric value. Current
registration/link/runtime documentation and API baseline were updated.

Runtime increment validation: all 2,660 solution tests passed; no tests were deleted in
this increment. Evidence: runtime-metadata-tests-final.log. Diff whitespace check passed.
The targeted metadata layers are now removed; broader authoring-guide cleanup remains.

Documentation follow-up: refreshed guides 14, 15, 39, 40 and 45. The C# definition
guide now clearly distinguishes ordinary native-DI hosting from optional revision
definitions. Removed nonexistent AddX families, activation-context examples, manual
application construction, FlowExecution usage and outdated single-link settings.
Guide 45 points to focused current authoring guidance instead of duplicating stale
instructions. The public API overview lists packages from eng/packages.json.
README reflects the actual 32 solution projects and optional expression directory.
Historical plans/reviews retain their historical content.

The actual C# blocks from guides 03/39 were extracted into an isolated source consumer,
compiled and run; startup returned IsApplied and shutdown completed successfully.
No production code changed. Documentation checks now expect FluxFlow.Core.Mapping
instead of the retired standalone mapping package.
All 111 Release/documentation checks passed (docs-release-tests.log); whitespace
validation passed. The full runtime suite was not repeated for documentation-only edits.

Final validation for this increment: 2,660 passed, zero failures, including 494 runtime
tests and both Core targets. Evidence: port-metadata-tests-final.log. API baseline and
diff whitespace checks passed.

Next: examine those remaining runtime API records and finish current documentation before expanding setup. Keep routing, lifecycle,
backpressure, error handling and revision isolation intact.
