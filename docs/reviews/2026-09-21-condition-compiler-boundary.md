# Condition compiler boundary

ApplicationConditionCompiler retained an optional ApplicationDefinition field, a second
constructor, and an address-taking Compile overload after ordinary setup had stopped using
them. This made shared compilation responsible for navigating a document as well as engine
resolution, bound parameters and compile-session reuse.

Removed that field, constructor and overload. The compiler now accepts only an engine name,
expression, input type and parameters. FlowJson.ApplyLinks and ApplicationLinkCompiler select
the source workflow's engine, falling back to the application engine, where they already
read the document. System outputs keep the application selection. Ordinary IConfiguration
setup already captures the same choice at Add and needs no change.

The selection logic is relocated to its document consumers, not eliminated. The improvement
is a single shared compilation contract with no document/address dependency. No new class,
selector framework, public API, runtime cache or provider was added. Bound-parameter handling,
per-session engine/compile caches, retained compilation failures and diagnostics are unchanged.

Validation: full Release suite passed all 2,000 executions, including ScopedExpressionTests,
ApplicationLinkCompilerTests, direct FlowJson use and SectionLinkRegistrationTests. The existing
API baseline passes unchanged. No compiler warnings/errors in the test build log.
Evidence: artifacts/cleanup-review-20260920/condition-boundary-tests.log.
No commit, push or publication.
