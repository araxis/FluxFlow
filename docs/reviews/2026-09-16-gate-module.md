# First redesigned component module: Routing / Gate

Physical source layout is src/Components/FluxFlow.Components.Routing, mirrored
by the src/Components Visual Studio solution folder. Each future component module
gets its own project/package there; no empty placeholder modules were added.
Tests stay under tests/Unit/FluxFlow.Components.Routing.Tests. The solution now has
ten projects and two production packages. Core does not depend on Routing.

Gate is one class wrapping FlowNode. It registers Input, Output, Open, Close and
the inherited Events stream directly. It starts closed unless initiallyOpen is
true. Closed data, including error envelopes, is discarded; open data is forwarded
without replacing the envelope/payload. Command payloads are ignored. IsOpen is
an observable state, not an acknowledgement protocol. Native SendAsync means
admission; independent inputs do not provide a combined command/data order.
An accepted output is not withdrawn by Close.

Construction/registration:

```csharp
var gate = new Gate();
var component = app.AddWorkflow("main").Add("gate", gate);
// JSON uses the same class:
bindings.Add("gate", _ => new Gate());
```

Gate needed ownership for ordinary extra inputs. FlowNode.AddInput(process) is a
small protected helper creating an ActionBlock with the node's native execution
options and existing processing/error/diagnostics boundary. It does not register
names automatically. The processing lifetime owns these blocks, closes them on
Complete, joins accepted work before closing outputs and faults all inputs on
failure. Terminal input semantics were not reused for Open/Close. No new base,
descriptor, options class, builder or handler class was introduced.

The package manifest, public API baseline, package metadata, changelog and layout
guards include Routing. The former Core-only release-plan assertion now expects
Routing after Core. The historical cleanup ledger remains historical; Routing is
the only deliberately reintroduced module. Earlier Core-only checkpoint counts
are superseded; other removed modules remain absent.

Validation: 2,399 passing tests in nine reports, zero failed/skipped. This adds
seven Gate cases and three extra-input cases on both frameworks (20 executions),
without removing cases. Coverage includes state transitions, exact forwarding,
closed drops, error-envelope behavior, native cancellation, fault propagation,
additional-input error recovery and joined shutdown. Release build has zero
warnings/errors. Baseline and whitespace checks pass.

Fresh package-only consumption passed on .NET 8.0.31 and .NET 10.0.12, including
JSON factory construction, addressed Open/Close, request/reply and shutdown.
The consumer verifies resolution of exactly the candidate Core and Routing
packages. eng/verify-routing-consumer.ps1 reproduces that qualification.

Evidence: artifacts/gate-20260916/qualified-tests/, qualified-consumer.log,
build-final.log and verification.json. Earlier failed package metadata/layout
expectations and the consumer's missing namespace import were corrected before
the final runs. No remote publication, commit or push.
