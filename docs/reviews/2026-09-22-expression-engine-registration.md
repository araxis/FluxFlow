# Readable expression engine registration - 2026-09-22

Implemented the approved API:

```csharp
services.AddJsonataExpressionEngine(useAsDefault: true);
services.AddCSharpExpressionEngine();
```

The old AddJsonataExpressions/AddCSharpExpressions names are removed. Each renamed
method accepts optional useAsDefault, false by default. It registers the existing
stateless, non-disposable Engine instance under its normal key and, when requested,
as the unkeyed IFlowExpressionEngine. Both registrations from that call reference
the same object. No new class, registry, setting model or Core change was needed.
The Compile helpers and engine implementation behavior are unchanged.

This uses the existing conditional-link default path. Explicit Engine keys win;
unknown explicit engines still fail Build. Mapper and Router continue to require
Engine; there is no application/workflow default setting. A false flag leaves the
host's unkeyed registration untouched. Multiple explicit defaults follow native DI's
last-registration-wins rule. No custom selection mechanism was added.

All production/test/package-consumer calls and current guides use the new names.
Corrected the engine READMEs' stale application/workflow ExpressionEngine examples.
Public source baseline updated for the intentional registration API rename/signature
change. Consumers must update calls and rebuild; no legacy forwarding overloads.

Added nine scenarios per Configuration test target: four optional-default/identity
cases, four real conditional-link routes covering both default engines and explicit
override, and Mapper rejection without Engine despite a default. Existing missing-key
and compilation-reuse scenarios remain. The independent Configuration package consumer
now routes through a real JSONata condition with its Engine omitted while retaining
explicit JSONata/C# Mapper and C# Router selection.

Validation logs are under artifacts/cleanup-review-20260920:
engine-registration-tests.log, engine-registration-api.log,
engine-registration-full-tests.log and engine-registration-package.log.
All **1,800 Release test executions passed**, including public API validation.
The isolated Configuration consumer, with both engine packages, passed on .NET 8
and .NET 10. No commit/publication.
