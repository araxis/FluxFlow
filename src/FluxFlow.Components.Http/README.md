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

## Security

By default the node accepts any absolute per-message URL, which preserves 1.x
behavior. Hosts that process untrusted message URLs should restrict where
requests can go, because `defaultHeaders` (often credentials) are attached to
every request:

```json
{
  "type": "http.request",
  "baseUrl": "https://api.internal.example",
  "restrictToBaseUrlOrigin": true,
  "allowedHosts": [ "api.internal.example", ".internal.example" ]
}
```

- `allowedHosts` (default empty = allow all): when non-empty, the resolved
  absolute URL host must match one entry. Entries match case-insensitively,
  either exactly or as a leading-dot suffix such as `.internal.example`.
- `restrictToBaseUrlOrigin` (default `false`): when `true`, absolute message
  URLs must match the `baseUrl` scheme, host, and port.

Violations are reported per message through the `Errors` port with kind
`UrlNotAllowed` and the message is dropped. Header names and values containing
CR, LF, or NUL characters are also rejected per message before the request is
sent.

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

## Design Metadata

This package exposes a package-owned `IComponentDesignMetadataProvider` for its
node types. Hosts can compose it through `ComponentDesignMetadataCatalog` to
populate palettes, editors, validation views, and documentation without
duplicating package descriptors.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
