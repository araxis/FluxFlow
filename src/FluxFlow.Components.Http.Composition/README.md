# FluxFlow.Components.Http.Composition

Composition registration and Designer metadata for the canonical HTTP client
node. The adapter resolves host-owned keyed `HttpClient` and optional
`TimeProvider` resources; it does not create clients, own their lifetime, or
choose transport, authentication, retry, redirect, TLS, proxy, or endpoint
security policy.

Existing definitions using `http.client` remain supported as a hidden alias;
new definitions and Designer palettes use `http.request`.

## Canonical Registration

```csharp
services.AddKeyedSingleton<HttpClient>(
    "Resources.External.ApiClient",
    httpClient);

registry.RegisterHttpNodes();
```

| Type | Node | Input | Output | Resources |
|------|------|-------|--------|-----------|
| `http.request` | `FlowContentHttpClientNode` | `HttpClientRequest` | `HttpClientResult` | required `client`, optional `clock` |

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
        "boundedCapacity": 32,
        "maxResponseBodyBytes": 1048576,
        "treatNonSuccessStatusAsError": false,
        "maxDegreeOfParallelism": 1,
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

## Typed Compatibility

Code-authored hosts can retain the released request/response contract under a
distinct node type:

```csharp
registry.RegisterHttpResponseOutput("http.request.response-output");
```

That explicit registration uses `HttpClientNode`, `HttpRequestInput`, and
`HttpResponseOutput` and retains the released Errors and Events surfaces. Use a
distinct type when canonical and compatibility registrations share a registry.

## Design Metadata

`HttpComponentDesignMetadataProvider` describes the canonical fixed ports,
runtime/limit/timeout option hints, required host-owned client picker, and
optional host-owned clock picker. Metadata remains descriptive. Hosts own
palettes, inspectors, validation UI, resource selection, persistence,
activation, and runtime status display.
