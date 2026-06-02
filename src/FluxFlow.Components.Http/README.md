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

## Runtime Timing

Responses and request errors use the package clock for timestamps and elapsed
milliseconds. Existing callers use the default system clock. Hosts and tests can
provide a deterministic clock through registration:

```csharp
registry.RegisterHttpComponents(options => options
    .UseClock(httpClock));
```

## Sender Ownership

The package includes a default per-node sender. Hosts that need named clients,
shared clients, custom authentication, tracing, proxy settings, deterministic
time, or test doubles can provide `IHttpRequestSenderFactory` through
registration:

```csharp
registry.RegisterHttpComponents(options => options
    .UseClock(httpClock)
    .UseRequestSenderFactory(myFactory));
```

The sender factory receives the resolved node options and configured clock. The
package disposes senders it creates through the configured factory.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
