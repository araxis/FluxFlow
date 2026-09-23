# Single-step processing lifetime construction

Previously, wrappers supplied output/events in the lifetime constructor and the
input later through Initialize. This allowed a partially initialized lifetime and
made it unclear where the input was supplied.

FlowNode and FlowTransformNode now construct their blocks first and supply
input, output and events together to FlowProcessingLifetime's constructor.
Initialize and the null-initialized processing field are removed. The optional
flush and drain callbacks are named arguments; transforms no longer pass an empty
flush callback.

Transform forwarding receives the stopping token as an argument rather than
reading the wrapper's lifetime field during construction. Lifetime observes drain
failures immediately and faults the blocks before joining them. This preserves
shutdown of blocked processing and avoids a constructor callback referring to a
lifetime that has not yet been assigned. Ordinary processing delegates cannot run
until the wrapper exposes its input after construction.

Additional inputs/outputs still join the lifecycle when declared by derived
components. Component ports remain constructed locally; naming/registration stays
external. No factory, interface or new abstraction was introduced. Public component
APIs are unchanged; the source declaration baseline reflects the internal helper.

Validation: Release build has zero warnings/errors; all 2,409 tests passed across
nine TRX reports. Existing native cancellation tests were strengthened to assert
output termination and event completion, both before construction and during active
processing. Fresh Core-only and Routing package consumers pass on .NET 8 and 10.
Evidence is under artifacts/lifetime-construction-20260916/. No commit/push/publication.
