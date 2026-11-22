# Configuration setup for every current component

## Coverage

The inventory contains eleven components in ten packages. Batch, Gate, Mapper,
Router, Scheduler and Source already had configuration adapters. Added the five
missing DTO/registrar pairs in their corresponding Configuration component folders:

- Filtering: ChangeFilter and Deduplicate.
- Reducing: Reducer.
- RateLimiting: RateLimit.
- Resilience: Retry.

Registration uses the existing IConfiguration traversal and transactional service
collection. It binds each component DTO once, snapshots its settings and registers
deferred native DI factories and typed port links. No component or provider is built
during registration. Core, Application and component implementations are unchanged.

## Adapter choices

ChangeFilter and Deduplicate compile non-null string-key selectors with the existing
message context. Ordinal equality is the default; IgnoreCase selects ordinal
case-insensitive equality. Retention, MaxKeys and the host TimeProvider retain
Deduplicate's existing behavior. Code-authored components remain generic.

Reducer reads InitialState through JsonSource during registration. Reduce and
AreEqual use ExpressionSource, compiled during Build. They receive state/input and
previous/current respectively, matching the runtime's value-only callbacks. No
synthetic message is created to supply metadata that those callbacks do not receive.

RateLimit resolves a native RateLimiter resource with the existing resource lookup.
The host provider owns it; the component borrows it. Hosts may register any native
limiter directly or bind its native options with ResourceRegistration. No replacement
limiter factory hierarchy or configuration policy enum was introduced.

Retry maps data fields to its native retry policy, preserving transient-error
defaults. Optional ShouldRetry compiles a reply predicate using an explicit Engine.
It evaluates both successful and error replies, so property access must match the
reply shape. It does not select thrown transport exceptions. Code-authored native
policies continue to support that separate capability.

Both request/reply adapters register their native Request output and terminal Reply
input. Standard Input/Output/Events/Error configuration uses the existing helpers.

Configuration now references all component packages. Its binder dependency was
raised from 10.0.7 to 10.0.12 to satisfy the existing resilience package dependency.
No direct resilience-engine package reference was added.

## Verification

- 72 new adapter test executions pass across .NET 8 and .NET 10.
- Full Release suite: 2,207 passed; the expected public API snapshot mismatch was
  updated for Configuration only and the snapshot test then passed (2,208 total).
- Tests cover registration snapshots, invalid settings and rollback, both expression
  engines, build-time file compilation, key filtering/expiry, reducer suppression
  and lineage, retry correlation, deferred limiter resources and lease ownership.
- Isolated packaged Configuration consumer passes on both targets, exercising all
  eleven components, request/reply links and input/output duplicate suppression.
- Package guide includes registration inventory, settings, expression variables and
  examples for every new adapter.

No commit or publication was performed.
