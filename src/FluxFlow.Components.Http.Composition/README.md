# FluxFlow.Components.Http.Composition

Composition registration and Designer metadata for the canonical HTTP client
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
services.AddFluxFlowComponents().AddHttp();
```

| Type | Node | Input | Output | Resources |
|------|------|-------|--------|-----------|
| `http.request` | `HttpClientNode` | `HttpClientRequest` | `HttpResponseResult` or `FlowError` | required `client`, optional `clock` |

The descriptor exposes Events and no universal Errors surface. Expected
request and transport failures are `HttpClientFailureResult` values on Output.
The runtime does not add an implicit mapper or serializer.

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

This optional application-integration adapter registers its immutable `ComponentDescriptor`
entries and explicit HttpComponentDefinition declarations through `IServiceCollection`:

```csharp
services.AddFluxFlowComponents().AddHttp();
```

The resulting `ComponentCatalog` is built once from DI registrations. Standalone
runtime nodes remain usable without this package, and referenced external resources
remain host-owned.

## Code-first authoring

`HttpComponents.HttpRequest` is the typed contract used by both generic `AddComponent` and `AddHttpRequest`. Its handle exposes named `Input`, `Output`, and `Events` ports. See [typed code-first authoring](../../docs/39-typed-code-first-authoring.md).

A definition built from this contract retains its executable descriptor. Normal
code-first hosting therefore calls only `AddFluxFlow(definition)` and does not
repeat the family registration above. Use that service registration for
JSON/configuration, catalog, or dynamic definitions that do not carry the
complete contract.
