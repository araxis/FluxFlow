# FluxFlow.Components.Sessions.Composition

Optional registrations and Designer metadata for session record, replay, and
query.

Metadata declares the runtime package's typed content/query contracts, Output,
and Events. Store, session name, replay/query, timing, and runtime options remain
flat. Store and clock references resolve host-owned keyed resources.

Errors share normal Output. There are no Sessions or Errors compatibility
ports, and Composition does not own the store.

## Registration And Design Metadata

Register components with `RegisterSessionQuery`, `RegisterSessionRecorder`, `RegisterSessionReplay`. `SessionsComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.
