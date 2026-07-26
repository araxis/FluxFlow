# FluxFlow.Components.Routing.Composition

Optional JSON registration and Designer metadata for `flow.window`,
`flow.correlate`, and `flow.join`.

The configured nodes use `JsonElement` specializations. Window has one Input;
Correlation has one Input and host-owned key/side selector delegates; Join has
Left and Right inputs with host-owned key selectors. Each exposes one typed
outcome Output and Events. Match, timeout, and error routing use normal link
conditions; there are no compatibility branch or Errors ports.

Selector resources use delegate picker hints and clocks use clock picker hints.
Composition does not own these resources.

## Registration And Design Metadata

Register components with `RegisterCorrelation`, `RegisterJoin`, `RegisterWindow`. `RoutingComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.
