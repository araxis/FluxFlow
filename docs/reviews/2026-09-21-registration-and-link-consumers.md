# Registration and link API consumer audit

## Removed unused registration path

Core.Configuration.Registration.ComponentRegistration and
FluxFlowRegistrationExtensions.AddComponent had no production caller or resolver
after the authored-definition removal. The optional Configuration project uses its
own ComponentRegistration(IServiceCollection, IConfigurationSection), selected by
Type during AddFluxFlowApplicationAsync. That active path is unchanged.

Removed the two Core files, the unused FactoryRegistrationFixture, its project
include, dead TestSink definition/type-registration helpers and the three tests
specific to the removed API (six executions across two target frameworks).
The actual terminal TestSink node and native registration remain.
No replacement delegate, registry or construction path was introduced.
Updated registration documentation and the intentional public API baseline.

## Link compiler finding and decision

Production configuration runs through ApplicationConfiguration -> LinkFactory ->
FlowJson.ApplyLinks -> DocumentLinks -> native ports.Link/LinkFallback.
ApplicationLinkCompiler has no caller in that runtime path. Its callers in this
repository are compiler tests and ApplicationLinkCompilationBenchmarks.

It nevertheless has a distinct public responsibility: returning a collection of
located diagnostics, normalized declarations and compiled conditions without
applying routes. DocumentLinks applies routes and throws on invalid input;
FlowApplication validates the executable graph. Those are not interchangeable
contracts. The compiler also accepts explicit system-output address/type pairs.

Retained ApplicationLinkCompiler and its result/diagnostic types in this step.
Deleting them merely because production does not call them would remove the
independent diagnostic API. No relocation or new abstraction was made. A future
compiler consolidation needs an explicit decision about that diagnostic contract
and tests preserving it; it is not automatically part of unused-code cleanup.

## Validation

- Release solution build: zero warnings and errors.
- Full Release suite: 1,986 passed, zero failed or skipped.
- Public API baseline updated and checked.
- Fresh isolated Core NuGet consumer passed on .NET 8 and .NET 10.
- Configuration tests passed on both targets, including section registration.
- No remaining source references to Core.Configuration.Registration.
- Whitespace check passed. No commit or publication.

Evidence: artifacts/cleanup-review-20260920/registration-{api,build,tests,package}.log.
