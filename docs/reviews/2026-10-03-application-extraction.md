# Application extracted from Core

## Boundary

FluxFlow.Application is an optional class library under src and the matching Visual
Studio solution folder. It owns the existing application/workflow model, typed
application addresses, resource/component/port registration, deferred links,
graph validation, shared completion coordination and standard hosting adapter.

Core retains messages and values, component wrappers and local lifecycle, request/reply,
mapping contracts and native instrumentation. It references neither Application nor DI
nor Hosting. Components continue to depend on Core, without application composition.
Configuration references Application and the component packages it configures.
Expression engines still reference Core and now declare their DI abstractions dependency
explicitly instead of receiving it transitively through Core.

No replacement catalog, registry, provider, wrapper hierarchy or compatibility aliases
were introduced. Existing address rules, reserved system addresses, keyed singleton
ownership, link predicates, broadcast behavior, completion and fault handling remain.
This is a responsibility/package extraction, not a new implementation of composition.

## Public boundary

Replace these namespaces and add a reference to FluxFlow.Application:

| Previous | Current |
|---|---|
| FluxFlow.Core.Application | FluxFlow.Application |
| FluxFlow.Core.Addressing | FluxFlow.Application.Addressing |
| FluxFlow.Core.Execution | FluxFlow.Application.Execution |

FlowMessage.WithHeaders snapshots replacement headers while preserving payload,
message/trace/correlation/causation identifiers and timestamp. It does not capture
ambient trace context or create a derived processing result. Event enrichment uses
this narrow public operation; Application has no friend access to Core internals.
Core's internal restoration/copy constructors remain internal.

The package manifest, source API baseline, solution references, current guides and
package-consumer scripts are updated. Existing composition consumer coverage now
belongs to the Application package consumer; the Core consumer uses native LinkTo
without DI and independently verifies processing, lineage, JSON and request/reply.
CI runs both Core and Application package acceptance.

## Validation

- Full Release suite: 1,980 passing executions across .NET 8 and .NET 10, zero warnings.
- Boundary checks verify assembly ownership and absence of Application/DI/Hosting
  references and Application friend access in Core.
- Header replacement regression covers identity, snapshot isolation, lazy payloads,
  ambient trace context, errors and null rejection.
- Isolated Core, Application and Configuration package consumers pass both targets.
- Public API baseline updated for the intentional package/namespace migration and
  WithHeaders addition. Dependent applications must update imports and rebuild.
- No commit, push or publication.

Local evidence: full-suite TRX files under the temporary flux-application-extraction-final
directory; package consumers report their isolated feed/restore evidence directories.
