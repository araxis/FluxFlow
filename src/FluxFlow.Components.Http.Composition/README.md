# FluxFlow.Components.Http.Composition

Optional `FluxFlow.Composition` registration helpers for the standalone HTTP node
from `FluxFlow.Components.Http`.

This package does not create `HttpClient` instances, own base addresses, configure
handlers, or choose retry/auth/security policy. The host or adapter DI registers
keyed `HttpClient` services. Composition definitions reference those keys as
resources.

## Registration

```csharp
services.AddKeyedSingleton<HttpClient>("primary", httpClient);

services
    .AddFluxFlowComposition(configuration)
    .RegisterNodes(registry => registry.RegisterHttpNodes());
```

## Node Types

| Type | Node | Required resource | Ports |
|------|------|-------------------|-------|
| `http.client` | `HttpClientNode` | `client` | `Input`, `Output` |

`clock` is an optional keyed `TimeProvider` resource for deterministic timestamps
and request timeout tests.

## Configuration

```json
{
  "FluxFlow": {
    "Composition": {
      "workflows": {
        "main": {
          "nodes": {
            "api": {
              "type": "http.client",
              "resources": {
                "client": "primary"
              },
              "configuration": {
                "boundedCapacity": 32,
                "maxResponseBodyBytes": 1048576,
                "treatNonSuccessStatusAsError": false,
                "maxDegreeOfParallelism": 1,
                "defaultTimeoutMilliseconds": 30000
              }
            }
          },
          "links": []
        }
      }
    }
  }
}
```

The composition package binds only `HttpClientNodeOptions`. HTTP method, URL,
headers, body, content type, and per-message timeout still come from
`HttpRequestInput` messages at runtime. Transport policy stays on the injected
`HttpClient`.
