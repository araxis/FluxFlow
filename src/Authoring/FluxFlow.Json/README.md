# FluxFlow.Json

Development preview of portable document execution on the instance-first Core graph. The flat Resources/Workflows/Type/Input/Output representation is unchanged. Direct Core construction requires neither constructor bindings nor this package.

This package owns the portable model in `FluxFlow.Json.Model`, declaration parsing and condition compilation. Its framework dependencies are Core and Mapping; it does not reference Composition, Engine, DI or Designer.

`FlowJson.BuildAsync(document, bindings)` invokes trusted type-to-constructor bindings, connects ports and compiles conditions without starting components. The host supplies dependencies and expression-engine configuration. `FlowJson.Serialize(document)` serializes portable data only.

Code authoring is optional: reference Composition and import `FluxFlow.Composition.Authoring` for `ApplicationDefinitionBuilder.BuildApplicationAsync(bindings)`. The builder returns `ApplicationSetup`, which separates the portable data from code-only links and activation contracts. Call `setup.ToDefinition()` before exporting authored links; this checked projection rejects native predicates and activation contracts rather than silently omitting them. Native predicates remain executable in memory.

Links default to `Reliable`: unconditional links stay compact strings such as
`"Output": "sink.Input"`. To select immediate admission, use
`"Output": { "Port": "sink.Input", "Delivery": "RejectWhenFull" }`.
`IsolateRejection` reports a rejection or failed condition without faulting the
graph. Delivery names and property names are case-sensitive; numeric enum values
are not accepted. `Condition` and `Parameters` can share the same link object.
Input-side declarations support the same policies. Policy survives code export,
compiled links, and editable document persistence.

The host must pass `onRejected` to `BuildAsync` / `BuildApplicationAsync` when a
direct Core document contains `IsolateRejection`. It receives source address,
target address, original message and original failure. It must return promptly;
it owns any asynchronous work it starts. Handler failures do not interrupt siblings.
Other policies do not call that handler. Hosted execution uses its existing
rejection stream instead. Conflicting policies for the same declaration are rejected.

## Resource ownership

`FlowJson.BuildAsync(document, bindings)` is the lightweight, host-bound entry
point. It rejects nonempty `Resources` before invoking any component binding:
the host supplies dependencies explicitly and retains their ownership. This
package does not implement a resource container. Keep the required JSON property
as `"Resources": {}` when no resources are declared; do not remove the property.

For documents that declare resources, reference the optional Composition package
and import `FluxFlow.Composition.Authoring`. Deserialize the same portable model,
then call `document.BuildApplicationAsync(applicationServices)` with the host's
registered component/resource contracts. This creates a normal Core
`FlowApplication`, not a live-update runtime. Its application scope owns the
resources it creates; parent host services remain borrowed. Engine and Designer
are not required for this path.

Component bindings must return fresh, unstarted instances. Parsing and unknown
type checks run before construction. Checks requiring actual native port maps
run after construction; failure disposes newly owned instances without starting
sources. Do not perform transport I/O in constructors. The optional composition
path can also validate its declared option/resource/port metadata before activation.

This preview still requires the repository's remaining release gates; resource
execution is an existing optional capability, not an unfinished Core migration.
