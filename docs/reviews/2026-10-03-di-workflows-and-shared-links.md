# DI workflow discovery and shared port configuration

## Completed

Workflow discovery now uses keyed FlowWorkflow singletons and an unkeyed alias for
enumeration. AddComponentFactory registers a ComponentAddress under its workflow key;
Build enumerates these addresses and resolves the canonical keyed IFlowNode. Empty
workflows, duplicate rejection, registration order and separate host providers remain
supported. Workflow discovery does not construct components or resources.

Removed ReadWorkflows, StoreWorkflows, the workflow/component-name dictionary,
FlowApplication.WorkflowMap, and FlowWorkflow.Components. Native port and event-address
discovery also uses keyed enumeration directly instead of replacing growing collections
in the service collection. No new registry, metadata model or provider was added.

FlowApplication retains a sequential list of successfully constructed nodes and event
bridges for cleanup. It never resolves more components during shutdown or failed-build
cleanup. DI alone disposes components/resources. Workflow aliases are non-disposable;
disposable nodes are not registered again under an enumeration alias. FlowWorkflow
contains its name and routing declarations. Routing state, bounded delivery, lifecycle
coordination and synchronization remain because they implement execution behavior.

## Shared configuration

OutputComponentConfiguration is a data-only record containing Output, Events and Error.
ComponentConfiguration adds Input. Scheduler uses the output-only contract; Source,
Gate, Mapper, Router and Batch use the input/output contract. MapperConfiguration and
RouterConfiguration are now sealed records to share this record hierarchy.

Each registrar calls AddLinks(componentAddress, config) after registering its native
ports. That helper delegates to the existing directional/error helpers and keyed link
identity. It handles the input-capable contract even when the caller holds the output
base type. There is no reflective port discovery or configuration behavior in the DTOs.
Gate Open/Close and Router Unmatched/dynamic outputs remain explicit registrar calls.
Custom DTOs and differently named ports can continue using individual link helpers.

Flat JSON, one/many syntax, relative/full typed addresses, conditions, engine selection,
duplicate suppression, configuration-only Error filtering and transactional application
registration are preserved. Runtime component implementations are unchanged.

## Public API migration

FlowApplication.Workflows is IReadOnlyCollection<FlowWorkflow>, replacing the indexed
dictionary snapshot. Iterate workflow.Name or resolve a named workflow from the live
provider with GetRequiredKeyedService<FlowWorkflow>(name). The collection is a read-only
snapshot and uses the same workflow instances as DI. Rebuild preview consumers.

Existing bundled DTO port properties remain available through their bases. Shared
records and AddLinks are optional for custom registrars. The source API baseline and
current documentation are updated. No compatibility lookup wrapper was introduced.

## Verification

- Full Release suite: 2,054 test executions passed, zero warnings in 28 test projects.
- Isolated Core and Configuration package consumers passed on .NET 8 and .NET 10,
  including keyed workflow identity before and after application construction.
- Added coverage for pre-Build workflow discovery, provider isolation, duplicate workflow
  rejection, repeated Build identity, single node disposal, disposal without construction,
  and refusing construction after Stop.
- Added delivery coverage for inherited Input registration through the output base type,
  including duplicate suppression with the direct two-port helper.
- Existing failed-build/cancellation, event observation, cross-workflow routing,
  conditions, error filtering, snapshots and drain tests pass.

No commit or publication is part of this change.
