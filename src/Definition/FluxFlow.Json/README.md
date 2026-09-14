# FluxFlow.Json

Use the ordinary `CallFunction` component with `RequestTo`, `ReplyFrom` and
`Timeout` to call a workflow path and emit its correlated reply on `Output`.
It is built into direct `FlowJson.BuildAsync` and Composition `BuildApplicationAsync`.
No function resource registration is required. The former `Resources.Functions`
section is rejected with a migration message. See
[examples and addressing rules](../../../docs/plans/2026-09-13-flow-functions.md).
Constants are deferred; there is no runtime constant store or placeholder expansion.
Configuration strings pass unchanged to the component's own binding.
Revision-aware hosting of CallFunction remains a separate step.

Resource objects have no separate Group model. Read whole JSON values or properties
without activating resources through `definition.GetResourceValue(reference, currentScope)`.
For example, `Resources.Connections.Messaging.Primary.Host` reads the configured Host;
`Host` with scope `Resources.Connections.Messaging.Primary` reads the same value.
Numeric path segments address array elements. Structural section names are normalized;
user-defined property names remain case-sensitive. This reads definition data, not
properties of a live connection.

Development preview of portable document execution on the instance-first Core graph. The flat Resources/Workflows/Type/Input/Output representation is unchanged. Direct Core construction requires neither constructor bindings nor this package.

This package owns the portable model in `FluxFlow.Json.Model`, declaration parsing and condition compilation. Its framework dependencies are Core and Mapping; it does not reference Composition, Engine, DI or Designer.

`FlowJson.BuildAsync(document, bindings)` invokes trusted type-to-constructor bindings, connects ports and compiles conditions without starting components. The host supplies dependencies and expression-engine configuration. `FlowJson.Serialize(document)` serializes portable data only.

Code definition is optional: reference Composition and import `FluxFlow.Composition.Definition` for `ApplicationDefinitionBuilder.BuildApplicationAsync(bindings)`. The builder returns `ApplicationSetup`, which separates the portable data from code-only links and activation contracts. Call `setup.ToDefinition()` before exporting authored links; this checked projection rejects native predicates and activation contracts rather than silently omitting them. Native predicates remain executable in memory.

Links default to `Reliable`: unconditional links stay compact strings such as
`"Output": "sink.Input"`. To select immediate admission, use
`"Output": { "Port": "sink.Input", "Delivery": "RejectWhenFull" }`.
`IsolateRejection` reports a rejection or failed condition without faulting the
graph. Structural property names and delivery names accept case variants; numeric enum values
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
package does not implement a resource container. Omit Resources or use
`"Resources": {}` when no resources are declared.

For documents that declare resources, reference the optional Composition package
and import `FluxFlow.Composition.Definition`. Deserialize the same portable model,
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

## Hand-written configuration

Reading then writing a document preserves property and entry order, including
root sections, workflow settings, the position of Type, resource objects, nested
option objects and mixed link arrays. Structural names are normalized to their
canonical spelling; user names, type identifiers, expressions and option values
retain their casing. Serialization may reformat whitespace; it is not a text editor.
Duplicate structural aliases such as Resources/resources are rejected.

Structural names (Resources, Workflows, Type, ExpressionEngine, standard
Input/Output/Events property names, Processing, DiscardUnmatched and link object
fields) accept case variants. Custom port names and address segments remain exact.
In component resource lookups and MQTT resource references, client means
Resources.client, group.client means Resources.group.client, and an explicit
Resources prefix is case-insensitive. Groups and empty Resources are optional.
Resolution never searches recursively for a leaf with a matching name. Existing
explicit host service keys remain supported by component activation.
