# Constants — deferred design idea

Decision (2026-09-13): the design is not mature enough to belong in the framework.
The experimental implementation has been removed, rather than kept behind a flag.

Removed application/workflow AddConstant/GetConstant, the runtime store,
DocumentConstants preparation and placeholder substitution, and the dedicated
section/addressing rules. Direct construction no longer serializes and reparses
the application document for expansion. There is no special Constants section
or placeholder/escape syntax in the framework. Strings such as ${...} and
$${...} are ordinary configuration values passed unchanged to component bindings.

Ordinary JSON resource values and general resource/property addressing remain.
A user-defined object name does not create a runtime constant facility.

The idea may be revisited with concrete usage examples. Questions to settle first:
whether a shared value belongs to host configuration or the flow framework;
whether build-time substitution or runtime access is actually needed; and how
either choice improves operations and component development without adding another
configuration language. No syntax, API, scope policy or implementation is reserved.

The historical implementation and validation are recorded in step 52 of the
observability/refactoring ledger; step 54 records their removal. Hot reload and
visibility remain separate, deferred decisions.
