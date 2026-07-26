# FluxFlow.Components.Observability.Composition

Optional JSON registrations and Designer metadata for Counter, Logger, and
Metrics.

Configuration-driven nodes use their `JsonElement` specializations and emit
typed snapshots or log entries on one Output plus Events. Errors are in-band.
Expression engines, context factories, selectors, attributes, and clocks are
host-owned keyed resources.

Metadata provides filtering, logging, metric, attribute, diagnostic, type, and
runtime hints. Resource key patterns support Designer pickers without changing
resource ownership or requiring Engine.

## Registration And Design Metadata

Register components with `RegisterCounter`, `RegisterLogger`, `RegisterMetrics`. `ObservabilityComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.
