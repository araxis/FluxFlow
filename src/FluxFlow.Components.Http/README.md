# FluxFlow.Components.Http

Reusable HTTP request components for FluxFlow.

## Nodes

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `http.client` | resource | Owns the HTTP sender lifecycle (base URL, host allow-list, redirects, timeout, pooling). Request nodes reference it by name. |
| `http.request` | `Input` -> `Output`, `Errors` | Sends HTTP requests through a borrowed client and emits typed responses or request errors. |

Register both node types once:

```csharp
registry.RegisterHttpComponents();
```

## Client resource

The `http.client` node owns the sender. It is a resource: `http.request` does
not build its own client, it borrows the established sender from an
`http.client` by name. Transport and security settings live on the client.

```json
{
  "type": "http.client",
  "name": "internal-api",
  "baseUrl": "https://api.internal.example",
  "defaultTimeoutMilliseconds": 100000,
  "followRedirects": true,
  "restrictToBaseUrlOrigin": true,
  "allowedHosts": [ "api.internal.example", ".internal.example" ],
  "pooledConnectionLifetimeSeconds": 300,
  "maxConnectionsPerServer": 20,
  "defaultHeaders": { "x-api-key": "..." }
}
```

`http.request` requires a `client` that names an `http.client` resource. The
reference is mandatory; there is no inline transport configuration on the
request node.

```json
{
  "type": "http.request",
  "name": "call-api",
  "client": "internal-api",
  "maxResponseBodyBytes": 1048576,
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

## Connecting (host-driven)

Connecting is an explicit host decision: there is no auto-connect or lazy
connect. `StartAsync` on the client is a no-op. The host establishes and tears
down the sender through `IHttpClientHandle`:

```csharp
await client.ConnectAsync(cancellationToken);
// ... run the graph ...
await client.DisconnectAsync(cancellationToken);
```

`http.request` borrows the established sender at call-time and never builds or
disposes it. A request sent before the client is connected is reported per
message on the `Errors` port rather than faulting the node.

## Security

A host that processes untrusted message URLs should restrict where requests can
go on the `http.client`, because `defaultHeaders` (often credentials) are
attached to every request:

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
milliseconds. The package uses `System.TimeProvider` (default
`TimeProvider.System`); there is no bespoke HTTP clock interface. Hosts and
tests can provide a deterministic `TimeProvider` through registration:

```csharp
registry.RegisterHttpComponents(options => options
    .UseClock(httpClock));
```

## Sender Ownership

The `http.client` resource builds a default pooled sender from its options.
Hosts that need custom authentication, tracing, proxy settings, or test doubles
can provide `IHttpRequestSenderFactory` through registration:

```csharp
registry.RegisterHttpComponents(options => options
    .UseClock(httpClock)
    .UseRequestSenderFactory(myFactory));
```

The sender factory receives the resolved client handle and configured clock.
The `http.client` resource disposes senders it creates through the configured
factory.

## Design Metadata

This package exposes a package-owned `IComponentDesignMetadataProvider` for its
node types. Hosts can compose it through `ComponentDesignMetadataCatalog` to
populate palettes, editors, validation views, and documentation without
duplicating package descriptors.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
