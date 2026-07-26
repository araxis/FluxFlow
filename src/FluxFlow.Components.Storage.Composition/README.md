# FluxFlow.Components.Storage.Composition

Optional registrations and Designer metadata for storage put/get/delete/query.

Descriptors use the runtime package's typed requests/outcomes, one Output, and
Events. Store references, names, query options, and runtime hints remain flat.
Stores and clocks are host-owned keyed resources. Errors share Output; there is
no Sessions or Errors compatibility branch.

## Registration And Design Metadata

Register components with `RegisterStorageDelete`, `RegisterStorageGet`, `RegisterStoragePut`, `RegisterStorageQuery`. `StorageComponentDesignMetadataProvider` supplies renderer-independent option, port, and host-owned resource hints for the Designer catalog.
