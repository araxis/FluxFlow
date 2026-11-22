# Synchronous configuration setup

## Changes

AddFluxFlowApplication returns IServiceCollection directly. The old async entry point,
ValueTask result and registration CancellationToken plumbing are removed. Component
and resource registrars remain synchronous delegates; no provider or component is
constructed during registration. Runtime build and lifecycle cancellation are unchanged.

Root sections are enumerated once. Component addresses use GetComponentAddress rather
than reconstructing the same address in traversal. Staged DI descriptors still preserve
atomic failure behavior, and the captured collection is frozen after success.

ReadRequiredConfiguration is a small extension over the existing optional reader. The
six bundled registrars use it instead of repeating missing-section exceptions. It includes
the DTO name and section path. Configuration properties, native conversions, defaults,
ConfigurationKeyName and scoped OneOrManyLinks behavior are unchanged.

## Reviewed and retained

The existing binding reader is already narrow: it handles top-level link properties and
delegates other conversions to .NET. Replacing it with a whole-object binder would still
need a way to exclude and resolve union-shaped links. No filtered configuration wrapper,
general binding framework, reflection cache or intermediate model was introduced.

Mapper and Router share expression source reading and message context preparation today.
Their remaining adapters differ: Mapper handles cancellation and returns a FlowValue;
Router builds named boolean routes. The conditional-link compiler also preserves cached
failures and explicit parameter type hints. A general compiler/preparation abstraction
would add more concepts than it removes, so these small adapters remain explicit.

Add-time settings validation is retained even where constructors also validate options:
configuration must fail before provider construction, while direct component callers
still require runtime option validation. Common port/link registration was already
consolidated in the preceding stage. Specialized ports remain visible in each registrar.

## Migration and verification

Replace awaited AddFluxFlowApplicationAsync calls with AddFluxFlowApplication, without
a registration cancellation argument. No compatibility forwarding API is retained.
Current examples, tests and the isolated package consumer use the synchronous API.
Registration-only tests now use synchronous assertions and methods. Existing integration
tests continue to exercise real deferred construction, links, conditions and disposal.
The required-reader checks cover default values, renamed link properties, nested native
conversions, missing-section diagnostics and null arguments.

Final Release suite: 2,054 test executions passed with zero warnings across 28 test
projects. Configuration passed 292 tests per .NET 8/10 target. The public API baseline
is updated and passing. The isolated Configuration package consumer passed on .NET
8.0.31 and 10.0.12. The final whitespace/diff check passed.

No commit or publication is part of this change.
