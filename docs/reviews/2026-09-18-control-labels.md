# Separate control ownership from application naming

The remaining terminal-control helper really coordinates shutdown: controls must
remain available while accepted downstream work drains, and faults must cancel and
join active actions. Native ActionBlock provides execution, but does not infer this
relationship between blocks. Removing the helper today would duplicate its lifetime
logic in FlowNode and FlowSource. It remains internal, pending further consolidation.

Removed a separate, misplaced responsibility: it no longer validates labels as
ApplicationAddress segments or indexes control blocks by name. A list owns the
actual blocks. FlowNode no longer reserves the diagnostic label Input. Labels remain
nonblank for diagnostics, may repeat, and may contain punctuation. Application
composition still owns actual port registration and address uniqueness.

Two cases on both target frameworks verify distinct native blocks with repeated
labels, including Input and a label that is not an address segment. Existing control
tracing, cancellation and staged-drain tests run in the full suite. No public API
signature change, new abstraction or tracing implementation change.

Evidence: artifacts/control-labels-20260918/. This is a responsibility cleanup, not
completion of the FlowNode/FlowTransformNode consolidation. Next examine explicit
emission, multi-output and flush requirements before changing those contracts.

Verification: Release build passed without warnings/errors; all 2,440 tests passed
in nine reports, including the public API baseline and existing control tracing.
