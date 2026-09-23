# Component implementation no longer registers application ports

Removed RegisterPorts from IFlowNode, FlowNode, FlowTransformNode, FlowSource and
Gate. Gate no longer depends on FlowComponent, names ports with strings or decides
how the application exposes its blocks. It retains only state, actual blocks and
processing behavior. No replacement interface, virtual hook or registration
attribute was added to components.

The existing workflow Add callback is now required. JSON ComponentBindings stores
the constructor together with an explicit typed registration callback; the existing
DI activation context already supports external HasInput/HasOutput registration.
Its graph adapter still maps configured ports in the composition layer. No new
setup subsystem, catalog, descriptor, factory class or type switch was introduced.
These caller migrations keep current functionality working; they do not settle
future setup design.

This is a breaking preview API change: Add(name, instance) becomes
Add(name, instance, ports => ...). JSON Add takes constructor and registration
callbacks; typed options use Add<TOptions, TNode>. Applications may choose different
names for the same block. Gate tests now expose Open/Close as Enable/Disable and
exercise those addresses. Reflection guards verify that Gate and the four core
contracts/bases no longer expose RegisterPorts, including inherited members.

Component input construction is finalized when the native business input (or
source output) is exposed, instead of when application registration runs. This
retains protection against adding terminal controls after publication without
coupling components to application setup. Declare ports during construction before
exposing them. TerminalInputs still represents lifecycle identity, not addressing.
Terminal control diagnostic labels are unchanged in this step.

Existing source/control drain tests, payload identity, fanout, error-as-data and
request/reply behavior remain. Test-only NodePorts helpers name standard ports in
fixture setup; custom fixtures' legacy registration methods are explicitly called
by test callbacks, never by the framework. No production reflection discovery or
fallback registration remains.

Validation: 2,409 tests passed in nine reports, zero failed/skipped. Ten executions
added for the registration boundary, none removed; existing registration tests
now verify required callbacks and external naming. Fresh Core-only and Routing
package consumers passed .NET 8/.NET 10. Both construct direct/JSON graphs through
external callbacks; component classes in the consumer no longer self-register.
The source API baseline was updated. No commit, push or publication.
Release build passed with zero warnings/errors; whitespace checks passed.

Evidence: artifacts/registration-boundary-20260916/qualified-tests/,
core-consumer.log, routing-consumer.log and verification.json. Earlier migration
failures remain in separate logs and are not included in the final totals.
