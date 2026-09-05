# Application events: scope and design

FluxFlow provides runtime facts. Consumers decide what those facts mean for dashboards, metrics, historical reports and business workflows.

## Public boundary

FluxFlowApplication.Events is an application-lifetime ISourceBlock<FlowMessage>, configured through FluxFlowApplicationOptions.Events (ApplicationEventOptions). It is available before activation and remains the same source across revisions and port-generation replacements.

There is one canonical representation: FlowEvent describes event data and ToMessage() produces a FlowMessage carrying FlowValue. Different event types may have different structured payloads. New types require neither inheritance nor report registration. Producer-owned typed payloads can be converted to FlowValue; the aggregator does not interpret their schema.

Message ID, trace/correlation/causation IDs, timestamp, payload and producer headers are preserved. The application adds its configured identity and the producing revision where known. Component and port producers supply source/workflow/component headers. Runtime-wide events may not have a component or revision; missing context is not guessed.

## Implementation

```text
Component event sources -> ComponentInstance.Activity --+
                                                       |
Runtime diagnostics/system events --------------------+--> application Events
                                                       |    BroadcastBlock<FlowMessage>
Revision lifecycle events (including pre-activation) --+          |
                                                          native LinkTo
                                                        /       |       \
                                                   dashboard  metrics  archive
                                                     consumer-owned
```

The Engine feature has only three files:

- ApplicationEventOptions: application label and capacity.
- ApplicationEventStream: canonical envelope enrichment and bounded live broadcast.
- ApplicationEventAttachment: native links and forwarding-block lifetime for one revision.

ComponentInstance.Activity uses a native broadcast, not a callback registry. Revision attachments use bounded ActionBlocks. Existing port event routes retain their operational responsibilities. Revision lifecycle phase remains in the existing revision event data; there is no competing reporting-phase model.

## Delivery and ownership

This is an observational feed, not a durable log. Bounded Post ingress can reject observations; BroadcastBlock keeps the latest value and slow targets can miss intermediate values. Producer event sources can also be lossy. Native links do not establish global causal ordering between concurrent producers.

The application completes its source when stopped or disposed. Consumers choose link predicates, target capacity, cancellation and completion propagation. Disposing a link detaches it; target cleanup remains consumer-owned. Avoid blocking predicates or custom targets whose OfferMessage blocks.

History, replay cursors, report definitions/catalogs, saved filters, aggregation and coordinated report snapshots are not library responsibilities. Existing State, Current and Ports.Status remain available; reading them is not a transactional event/history snapshot. Existing operational instrumentation remains unchanged.

## Preview migration

The unshipped reporting API is replaced, not retained as a compatibility facade:

- Reporting.Events -> application.Events, now carrying FlowMessage directly.
- Reporting options -> Events options; replay capacity no longer exists.
- ObserveEvents callbacks -> ComponentInstance.Activity.LinkTo.
- Report wrappers, catalog, definitions, replay journal and GetReportingSnapshotAsync are removed from Engine.
- The non-packaged reporting sample owns its report/filter/archive contracts and tests.

The consumer example retains historical queries, DI report selection, portable filters and frozen archive cutoffs. Its capture sequence describes received observations only, so it explicitly reports unknown upstream completeness.
