# Link registration while reading component sections

Normal configuration now registers link delegates in the existing per-component
loop, immediately after that component registers its factory and native port selectors.
Only properties matching registered ports are interpreted as links. The shared
declaration parser handles both directions, arrays, relative/full addresses, named
ports, conditions, parameters, delivery and fallback. Unique declarations register
ordinary LinkFactory delegates in the staged IServiceCollection.

Build invokes those delegates, resolves native ports and compiles conditions using
one provider-owned compilation session. The source workflow selects the expression
engine even when the link was declared on a different workflow's input. Repeated
Build neither reparses configuration nor recompiles conditions. No component or
expression engine is constructed during registration.

DocumentLinks is physically removed. Its required parsing, duplicate-policy checks
and link application logic are shared static methods on the existing parser/FlowJson.
No replacement builder, catalog or runtime scanner class was introduced. Core grants
the Configuration assembly internal access to reuse that implementation without adding
another public setup API. Temporary registration-time port-name sets contain no blocks
and are not retained as runtime lookup storage.

The public FlowJson.ApplyLinks document operation is preserved for explicit callers;
it enumerates their supplied document. Normal AddFluxFlowApplicationAsync no longer
calls it or registers an application-wide document-scanning delegate. The independent
ApplicationLinkCompiler diagnostic API remains unchanged.

Syntax and conflicting-policy errors now occur during registration and leave the
host collection unchanged. Target resolution and expression compilation remain at
Build, allowing forward references. No automatic discard or error collector was added.

Validation: 1,994 full Release test executions passed; solution build has zero
warnings/errors. New tests cover deferred creation/compilation, per-link registration,
input/output duplicate handling, arrays, cross-workflow engine selection, fallback,
configuration snapshots and transactional rejection. Existing delivery, lifecycle
and configuration tests remain. Public API source baseline checked and updated.
Isolated Configuration package consumer passed on .NET 8 and .NET 10.

Evidence: artifacts/cleanup-review-20260920/section-links-{final,focused,api,build,package}.log.
No commit or publication. This changes link-registration timing, not the broader
configuration document reader or the public diagnostic contract.
