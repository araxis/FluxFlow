# FluxFlow.Components.Http

Reusable HTTP request components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `http.request` | `Input` -> `Output`, `Errors` | Sends HTTP requests and emits typed responses or request errors. |

## HTTP Request

```json
{
  "type": "http.request",
  "name": "call-api",
  "baseUrl": "https://example.test",
  "defaultTimeoutMilliseconds": 30000,
  "maxResponseBodyBytes": 1048576,
  "followRedirects": true,
  "treatNonSuccessStatusAsError": false,
  "boundedCapacity": 128
}
```

`http.request` consumes `HttpRequestInput` values and emits
`HttpResponseOutput` values. Network, timeout, cancellation, invalid URL, and
body size failures are emitted through `HttpErrorOutput` on the `Errors` port.
The node continues processing later messages after a per-message failure.

Non-success status codes do not fault the node. Responses are emitted with
`Success = false`. When `treatNonSuccessStatusAsError` is enabled, the response
is still emitted and a matching error item is also emitted.

## Sender Ownership

The package includes a default per-node sender. Hosts that need named clients,
shared clients, custom authentication, tracing, proxy settings, or test doubles
can provide `IHttpRequestSenderFactory` through registration:

```csharp
registry.RegisterHttpComponents(options => options
    .UseRequestSenderFactory(myFactory));
```

The package disposes senders it creates through the configured factory.
