# One collection for processing outputs

Reviewed `FlowNode`, `FlowSource`, `FlowProcessingLifetime` and terminal controls.
Source shutdown has a real two-phase requirement: production and accepted data
outputs drain before downstream terminal controls close. Combining both base
classes into a new general lifetime abstraction would need policies for that
difference; this step does not introduce one or remove the behavior.

`FlowProcessingLifetime`, shared by FlowNode and FlowTransformNode, now owns the
main output and additional outputs in one collection, as FlowSource already does.
Fault, completion, disposal and drain waiting each traverse that collection instead
of treating the main output separately. Events keep their existing separate
best-effort lifecycle. Initialization registers the main output before observation
or processing-completion monitoring begins. The forwarding drain task is passed
directly to completion monitoring rather than stored as mutable lifetime state.

This is a small internal ownership cleanup. There is no new class, public API,
project or component. FlowSource and FlowControlInputs are unchanged; source
production/control phases, flush-before-output-completion, error propagation and
resource ownership remain required behavior. Existing processing-lifetime,
multi-output, source-shutdown and control-drain tests cover these boundaries.

Evidence is under `artifacts/output-lifetime-20260915/`; the pre-step production
file is saved there. No package consumer, benchmark, commit, push or publication
is part of this step. No performance improvement is claimed.

Validation passed: 2,387 tests in seven reports, zero failures/skips; Release
build has zero warnings/errors. Public API baseline and whitespace checks passed.
See `verification.json`, `tests/`, `tests.log` and `build.log` in that evidence
directory. No tests were removed or added in this internal cleanup.
