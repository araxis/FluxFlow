# Component registration cleanup

This is item 3 of the Application review. AddComponentFactory formerly registered both
a keyed ComponentFactory delegate and a keyed IFlowNode factory capturing that delegate.
No production caller resolved the delegate. Configuration used its descriptor only to
check that registration supplied a factory.

The separate delegate service is removed. The synchronous ComponentFactory signature
and AddComponentFactory API remain; the native keyed singleton stores the factory and
owns construction, caching and disposal. Configuration now checks the actual deferred
singleton IFlowNode descriptor. A standalone delegate without a node factory is rejected
transactionally. Direct users of keyed ComponentFactory resolution must migrate to keyed
IFlowNode resolution. Workflow/address discovery stays in existing native DI registrations.

Input and output registration share one private generic helper, preserving native block
service types and address discovery. The existing address constructor validates port
names; registration no longer repeats it. Invalid names still reject before mutation or
selector invocation. Event registration keeps its distinct bridge behavior, with its
single-use registration helper inlined and typed address retained until DI key boundaries.

No new production types or registry were added. Same-instance ownership protection,
disposable-port rejection, duplicate-address checks, named events and lazy selectors remain.
Existing tests cover single construction across direct port resolution and Build, separate
providers, duplicate rejection and provider-owned disposal. Added coverage rejects
standalone factory-only configuration and invalid names before registration changes.

Validation: 2,057 Release test executions pass on .NET 8/10, with no warnings.
All 32 Debug projects build without warnings or errors. Isolated Application and
Configuration package consumers pass on both frameworks. The source-declaration
snapshot was reviewed and updated solely for the two port methods' expression-body
style; its declaration count remains unchanged. `git diff --check` passes.

Local evidence: `%TEMP%/flux-registration-simplification-final`,
`%TEMP%/fluxflow-application-consumer-346b267eee6d4f9ba4281d45babbdee2`, and
`%TEMP%/fluxflow-configuration-consumer-51151983a493414babfaaa5be170ecb5`.
No commit or publication in this step.
