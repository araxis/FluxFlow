# FluxFlow.Components.Http.Composition

Runtime registration has no Designer dependency. Visual consumers add
FluxFlow.Components.Http.Designer and call AddHttpDesign after AddHttp.
Direct constructors and runtime behavior are unchanged.


Structural registration and portable definition for the canonical HTTP client
node. The adapter resolves host-owned keyed `HttpClient` and optional
`TimeProvider` resources; it does not create clients, own their lifetime, or
choose transport, authentication, retry, redirect, TLS, proxy, or endpoint
security policy.

Definitions and Designer palettes use the exact canonical type `http.request`.
The retired `http.client` value is rejected and must be migrated before load.

## Canonical Registration

```csharp
services.AddKeyedSingleton<HttpClient>(
    "Resources.External.ApiClient",
    httpClient);
services.AddHttp();
```

| Type | Node | Input | Output | Resources |
|------|------|-------|--------|-----------|
| `http.request` | `HttpClientNode` | `FlowValue` | `FlowValue` or `FlowError` | required `client`, optional `clock` |

The descriptor exposes Events and no universal Errors surface. Expected
request, materialization, and transport failures are `FlowError` messages on
Output. The node owns request materialization; composition does not insert a
hidden mapper or serializer block.

## Flat Definition

```json
{
  "Resources": {
    "External": {
      "ApiClient": {
        "Type": "host.http_client",
        "baseAddress": "https://api.example.com/"
      }
    }
  },
  "Workflows": {
    "OrderProcessing": {
      "BuildRequest": {
        "Type": "order.http_request",
        "Output": "CallApi.Input"
      },
      "CallApi": {
        "Type": "http.request",
        "client": "Resources.External.ApiClient",
        "maxResponseBodyBytes": 1048576,
        "treatNonSuccessStatusAsError": false,
        "defaultTimeoutMilliseconds": 30000,
        "Output": ["HandleResult.Input", "Audit.Input"]
      },
      "HandleResult": {
        "Type": "order.http_result"
      },
      "Audit": {
        "Type": "audit.result"
      }
    }
  }
}
```

Resources, node options, resource references, and links use the canonical flat
document shape. The referenced `host.http_client`, request builder, result
handler, and audit types are host examples rather than types supplied by this
package. The host resolves the exact `Resources.External.ApiClient` address as
a keyed `HttpClient`.

`HttpClientRequest` carries method, URL, headers, optional exact `FlowContent`
body, and optional per-message timeout at runtime. Link conditions or mappers
can branch on `IsError`, `Kind`, `Error.Code`, response status, or other result
content without a special error edge.

Invalid numeric options fail activation and surface as composition factory
diagnostics when build failures are configured as diagnostics.

## Design Metadata

Hosts should compose this provider through `ComponentDesignMetadataCatalog`.
The canonical catalog adds the traced `Events` output and an optional semantic
`processing` profile picker, exposes `boundedCapacity` as an advanced runtime
control, and omits legacy `name`, `maxDegreeOfParallelism`, and `ensureOrdered`
options from normal editing. Default execution requires no processing profile.


`HttpComponentDefinition` describes the canonical fixed ports,
runtime/limit/timeout option hints, required host-owned client picker, and
optional host-owned clock picker. Metadata remains descriptive. Hosts own
palettes, inspectors, validation UI, resource selection, persistence,
activation, and runtime status display.

## DI Registration

This adapter registers direct node factories through `IServiceCollection`:

```csharp
services.AddHttp();
```

The resulting type-to-factory dictionary is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first definition

`AddHttpRequest` retain typed options and named port handles. They embed the
same callbacks used by DI in ApplicationSetup.ComponentFactories. Build the setup
with application services or pass it to AddFluxFlowUpdates. Repeating the family
DI registration is optional: matching delegates are idempotent and conflicts fail.
Links validate the actual ports after node construction. Factories remain in process
and cannot be exported as JSON; the flat JSON configuration shape is unchanged.

The former HttpComponents contract container is removed. Standalone node
construction remains available. Shared host resources are not disposed by components.

The callback binds options with ParallelRelaxedOrder capability. Parallel execution is supported; a preserving-order parallel profile remains unsupported.
