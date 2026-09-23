# Current component development

The current authoring path is documented in these focused guides:

1. [Node authoring](03-node-authoring.md): native blocks, message lineage, errors,
   backpressure and component lifecycle.
2. [Package authoring](04-package-authoring.md): explicit dependencies, local options
   and separate configuration adapters.
3. [Component registration](40-component-registration.md): native keyed DI factories,
   port addresses, links and ownership.
4. [Hosting](05-hosting-and-observability.md): ordinary startup and optional revisions.

Keep component behavior separate from setup. All addressable blocks carry FlowMessage;
FlowMessage<T> retains CLR values constrained to FlowValue. A component interprets its
own data and owns its blocks. DI owns the component. Registration assigns unique addresses
and selects blocks; it does not construct nodes or add metadata/activation wrappers.

Use the current checkout and coordinated package versions. Older migration plans are
historical evidence, not an alternative implementation guide. Optional expression
engines live in their own projects; deployment exporters, persistence and external
transport modules should not be inferred from the older documents.
