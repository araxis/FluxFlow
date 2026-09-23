# Source capacity without an options wrapper

Removed FlowSourceOptions. It held only OutputCapacity, so FlowSource now takes
`int outputCapacity = 128` directly. Example: `class MySource : FlowSource` with
constructor `MySource() : base(outputCapacity: 2)`. Default construction is unchanged.
Positive capacity validation occurs before output blocks are allocated. Source
production, output ownership, backpressure, terminal controls and shutdown are
unchanged. FlowNodeOptions and the three existing node/source bases still remain.

This is a breaking preview constructor/API change. Existing code passes its
capacity directly instead of constructing FlowSourceOptions. Error parameter names
now identify outputCapacity on that constructor. Capacity rejection and source
shutdown tests migrated. Two tests of the removed options object's default/getters
were removed per framework (four executions); runtime behavior tests remain.

The standalone package consumer now creates a capacity-one source, starts it,
receives its output through a native Dataflow block and awaits source completion.
Evidence lives under artifacts/source-capacity-20260915/. No commit/push/publication.
This is a configuration simplification, not completion of the common-base design.

Qualification: 2,373 passing tests in seven reports, zero failed/skipped; Release
build zero warnings/errors. Fresh package-only consumers passed net8.0/net10.0.
Public API baseline and whitespace checks passed. See verification.json,
qualified-tests/, build.log and consumer.log. Earlier incomplete attempts are
not included in the final test count.
