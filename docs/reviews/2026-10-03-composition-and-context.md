# Composition and message context cleanup

## Completed changes

Application construction now creates components, then resolves each declared input
and output once before making links. Previously it enumerated both complete port
lists and reparsed every address for every component. The traversal is now linear
in component and port counts, without adding a lookup table.

The existing DI discovery collections retain the validated ApplicationAddress
objects created at registration. Native block keys still use Address.Value. Output
routing and workflow selection use these values without parsing them again. Event
bridge discovery remains unchanged. Public registration signatures are unchanged.

This changes failure ordering: all component factories run before ordinary port
selectors are checked. Invalid unlinked ports still fail Build before link factories
run; created components are stopped and remain owned by DI for disposal. A declared
port with a missing component now also fails during Build instead of potentially
remaining unresolved. Regression tests cover invalid inputs and outputs, cleanup,
and single disposal after this ordering change.

FlowMapContext.ForMessage now owns one fresh variable dictionary directly. It no
longer creates an unused empty dictionary or copies its own populated dictionary.
Caller-supplied Variables still receive a defensive ordinal-keyed snapshot. Values
are shallow references, JSON remains lazy, and each evaluation has its own variables.
The explicit parameterless constructor preserves the existing construction API;
the source-declaration baseline now records it.

## Verification

- Full Release suite: 2,046 executions passed, zero warnings across 28 test projects.
- Core: 259 cases per target; Configuration: 291 cases per target on .NET 8 and 10.
- Existing context snapshot/isolation tests and expanded message-context assertions pass.
- Public API source baseline reviewed and updated; no new public capability.
- Isolated Core package consumer passed on .NET 8.0.31 and .NET 10.0.12.
- No throughput benchmark was run; allocation and traversal improvements describe
  removed work, not a measured end-to-end speedup.

## Next review findings

The migration recommended below was subsequently completed in
[DI workflows and shared links](2026-10-03-di-workflows-and-shared-links.md).

Workflow Engine now shares ordinary Input/Output/Error configuration and registers
those links through one registrar helper. Its application resolves registered
workflows directly from DI. These ideas fit FluxFlow, with the following boundaries:

1. Replace ReadWorkflows/StoreWorkflows and their reconstructed registration
   dictionary with ordinary keyed workflow registrations and enumeration.
2. Remove FlowApplication.WorkflowMap and workflow component dictionaries as
   independent service lookup authorities. Preserve cleanup of components already
   constructed when a later factory fails; cleanup must not instantiate new nodes.
3. Share standard port configuration and one registrar-level link helper. Keep
   Scheduler source-only, Gate Open/Close and Router dynamic routes explicit.
   No intermediate application model or whole-document link scanner is needed.
4. Keep FluxFlow's structural link identity, conditions, engine selection and
   Error filtering. Workflow Engine's separate physical error ports and its
   concatenated source-target key are not equivalent contracts.

These are review recommendations, not additional completed migrations. DI owns
service identity and component disposal; routing completion, draining, fault
propagation and tracking successfully constructed nodes remain execution concerns.

The Workflow Engine comparison also confirms keeping port registration separate
from the explicit link-registration step. Do not infer links through new attributes
or reflection conventions, or create invisible runtime components. Any whole-graph
validation must run after all workflows are registered, so forward and cross-workflow
references remain possible. Its requirement for every ordinary input to have an
incoming edge is not automatically a FluxFlow requirement. Aliasing disposable
components under additional DI registrations also needs care: returning the same
instance does not by itself guarantee a single disposal.
