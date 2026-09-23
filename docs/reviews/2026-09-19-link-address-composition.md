# Address-based link composition

Reviewed Workflow Engine's Setup/Registration/LinkRegistration.cs, Link.cs,
ILinkBuilder.cs and App/Workflow.cs in C:/Projects/dip-workflow-engine. Its useful
boundary is deferred registration in DI, followed by address-based resolution of
existing blocks. FluxFlow already has that boundary through LinkFactory. Copying
the ILink/Link/ILinkBuilder/LinkBuilder<T> hierarchy would add concepts without a
demonstrated need. The reference project was not modified.

## Change

Link delegates can now be written as:

```csharp
services.AddSingleton<LinkFactory>(ports =>
{
    ports.Link("main.source.Output", "other.sink.Input",
        message => !message.IsError);
    ports.LinkFallback("main.source.Output", "other.errors.Input");
});
```

These are extensions in the existing FlowExtensions class. Link and LinkFallback
retain delivery-policy and rejection-handler arguments; DiscardUnmatched explicitly
drops messages without an ordinary or fallback route. Addresses are full canonical
port addresses. JSON still resolves relative references using its existing scope
rules before calling these helpers.

DocumentLinks and FlowJson's native-predicate path now share the same lookup and
declaration methods. DocumentLinks indexes registered input/output names by their
existing owner once instead of scanning and slicing all addresses per component.
No custom registry or lookup class was added. Configuration examples, component
registration documentation and the public API baseline were updated.

The Build-only provider contains existing instance registrations. It does not own
components and must not be captured for runtime use. Host dependencies still use
transient LinkFactory registrations resolved in the caller's scope. Build prepares
routes; Start connects sources. No components are constructed while reading config.

## Actual simplification and limits

Authors no longer need to name FlowInputEndpoint/FlowOutputEndpoint to make an
address-based link, and JSON no longer repeats their resolution code. The original
endpoint APIs remain available. This is an additive authoring improvement and
small implementation deduplication, not the removal of an architectural layer.

FlowInputEndpoint, FlowOutputEndpoint, FlowLink and FlowRouter are retained. They
carry delivery policy, fallback selection, links and completion dependencies.
Replacing their behavior with per-link PropagateCompletion would not preserve
coordinated completion when multiple producers feed one consumer. Fanout, bounded
delivery, isolated rejection, source/control ordering and shutdown are unchanged.
No new LinkBuilder interface, class, delegate or transport algorithm was introduced.

## Validation

- Release solution build: zero warnings/errors.
- Full suite: 2,525 passed, zero failed/skipped, 29 TRX reports. Previous stage:
  2,517; four new theory cases run on both net8.0 and net10.0.
- New cases verify address-based conditions, fallback, explicit discard and
  missing source/target diagnostics with rollback and exactly-once node cleanup.
- Existing scoped-dependency/fanout/drain test now uses address helpers and builds
  twice, checking component identity, host ownership and disposal ordering.
- Existing JSON delivery-policy, backpressure, fallback and revision suites pass.
- Fresh Core package-only consumer passed on net8.0/net10.0, running the same
  workflow through document links and explicit address delegates. Candidate:
  0.0.0-local.20260919201752. No performance measurements were made.
- Diff whitespace check passed.

Evidence: artifacts/link-address-composition/ (build.log, api.log, tests.log,
tests/, verification.json, package.log and core-consumer/).

Changes remain local. No commit, push, publication or external project adoption.
