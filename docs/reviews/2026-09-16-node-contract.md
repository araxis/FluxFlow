# One node contract for component authors

IFlowComponent is removed. Its RegisterPorts method and TerminalInputs property
now belong to IFlowNode, alongside completion, faulting and disposal. Component
authors no longer choose between two overlapping interfaces. IFlowSource remains
the source-specific extension for startup and phased shutdown.

Registration works in either location: implement RegisterPorts(FlowComponent)
inside the component or use workflow.Add(name, node, ports => ...). The explicit
callback takes precedence. The default RegisterPorts throws a descriptive error
if neither path is provided; failed registration does not take ownership or reserve
the name. TerminalInputs defaults to empty and still identifies native block
instances rather than guessing from port names.

Direct graph construction, JSON factories, composition graph views, runtime drain
planning, tests and benchmarks use IFlowNode. Runtime interface tests/casts for
IFlowComponent are removed. Native processing blocks, error handling and source
shutdown algorithms are unchanged. The three concrete authoring bases remain;
this is not completion of their common-base proof.

Migration: replace IFlowComponent with IFlowNode. Existing RegisterPorts and
TerminalInputs implementations retain their bodies. A source need only declare
IFlowSource, which already inherits IFlowNode. If a derived class provides an
interface member that its parent did not implement, re-declare IFlowNode on that
derived class so C# remaps the default interface implementation, or use a virtual
implementation in the parent. This is a breaking preview API change, with the
source-declaration baseline updated.

New tests cover rejected missing registration and retry with the same name, plus
automatic registration and explicit callback precedence. Existing control-identity
and staged shutdown coverage is retained. The standalone package consumer uses
both registration paths with a native-block component, plus JSON/error recovery.

Evidence: artifacts/node-contract-20260916/. No commit, push or publication.

Qualification: all 2,379 tests passed in seven reports (four additional executions,
none removed). Release build has zero warnings/errors. Fresh package consumers
passed net8.0/net10.0; public baseline and whitespace checks passed. Final results
are in qualified-tests/, build-final.log, consumer.log and verification.json.
