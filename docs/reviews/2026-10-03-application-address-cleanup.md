# Application address construction cleanup

This pass implements item 6 of the Application review. Registration, linking and
resource lookup already use canonical address Value strings at native DI boundaries.
There is no dotted-address parser outside ApplicationAddress in the Application
project, so no replacement registry, key type or lookup helper is needed.

ApplicationAddress.Parse and full ResolvePort now share a private FromSegments
method. ResolvePort previously split and validated its reference, then called Parse
to split and validate the same text again. It now passes those validated segments
directly to the existing address-kind selection logic. Relative resolution also
constructs directly from the validated workflow and reference segments.

WorkflowPort validates its three arguments and constructs the port directly. It no
longer builds a temporary WorkflowComponent object and its canonical string merely
to read the first two segments back. Validation order and argument names remain.

No public signatures, equality rules, casing rules, DI keys, resource fallback or
reserved system addresses change. Resource-specific boundary checks remain required;
an ApplicationAddress can represent other kinds of address. No new production type,
address cache or hierarchy is introduced. This removes identifiable redundant work;
it does not claim a measured throughput improvement.

Regression coverage checks construction/parse/relative/full identity, hash agreement,
invalid constructor argument diagnostics, malformed full references, resource
rejection and reserved system address rules. Existing configuration, typed link,
duplicate-registration and resource-resolution tests remain in place.

Validation: all 2,099 Release test executions pass on .NET 8/10 without warnings;
the source public API baseline passes unchanged. All 32 Debug projects build without
warnings or errors. The isolated Application package consumer passes both frameworks.
`git diff --check` passes. No commit or publication in this step.

Local evidence: `%TEMP%/flux-application-address-cleanup` and
`%TEMP%/fluxflow-application-consumer-eb28352cc986409da8656cc5712c97aa`.
