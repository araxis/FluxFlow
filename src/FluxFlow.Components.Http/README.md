# FluxFlow.Components.Http

A reusable HTTP request component for FluxFlow.

## Node

| Node type | Shape | Purpose |
|-----------|-------|---------|
| `http.client` | `Input` -> `Output`, `Errors` | A "blockified" `HttpClient`: a request arrives on `Input`, is sent through the host-injected `HttpClient`, and the response is broadcast on `Output` (failures on `Errors`). |

The node owns no transport policy. Base address, connection pooling, redirects,
default headers, TLS, proxy, and any allow-list / SSRF guard all live on the
`HttpClient` you inject — exactly as you would configure a regular .NET
`HttpClient` (typically through `IHttpClientFactory` and a `DelegatingHandler`).
The node never disposes the client; the host owns its lifetime.

## Registration

The host supplies the `HttpClient`. A single shared client:

```csharp
registry.RegisterHttpComponents(options => options
    .UseHttpClient(httpClient));
```

Or a resolver, so different nodes can use different clients via the node's
optional `client` name (for example bridging to `IHttpClientFactory`):

```csharp
registry.RegisterHttpComponents(options => options
    .UseHttpClient(name => httpClientFactory.CreateClient(name ?? "default")));
```

A deterministic clock for timestamps/elapsed can be provided through
`UseClock(TimeProvider)` (default `TimeProvider.System`).

## Node options

```json
{
  "type": "http.client",
  "name": "call-api",
  "client": "internal-api",
  "maxResponseBodyBytes": 1048576,
  "treatNonSuccessStatusAsError": false,
  "boundedCapacity": 128,
  "maxDegreeOfParallelism": 1,
  "defaultTimeoutMilliseconds": null
}
```

- `client` (optional): name passed to the host's `HttpClient` resolver.
- `maxResponseBodyBytes`: response bodies larger than this are read up to the cap
  and the response is flagged `BodyTruncated = true`.
- `treatNonSuccessStatusAsError`: when `true`, a non-success status is reported on
  the `Errors` port instead of `Output`.
- `defaultTimeoutMilliseconds` (optional): per-request timeout used when the
  request input does not specify one. Null defers to the `HttpClient`'s own
  timeout.

## Behaviour

The node consumes `HttpRequestInput` and emits `HttpResponseOutput`. A relative
`Url` resolves against the injected client's `BaseAddress`; an absolute `Url` is
used as-is. Network, timeout, cancellation, and invalid-URL failures are emitted
as `HttpErrorOutput` on the `Errors` port and the node keeps processing later
messages. Non-success status codes do not fault the node.

`Output` and `Errors` are broadcast ports: every linked consumer receives every
item.

## Security

This node is a thin wrapper over the `HttpClient` you give it. Any policy for
untrusted URLs — host allow-lists, blocking redirects, pinning to an origin,
stripping credentials — belongs on that `HttpClient`, idiomatically as a
`DelegatingHandler` in its handler chain. Keeping it there means the policy
applies no matter who sends to the node, and the node stays a pure transport
pump.

## Design Metadata

This package exposes a package-owned `IComponentDesignMetadataProvider` for its
node type. Hosts can compose it through `ComponentDesignMetadataCatalog` to
populate palettes, editors, validation views, and documentation without
duplicating package descriptors.

## Composition Guidance

Use this package as one part of a host-composed graph. See
[Component Composition](../../docs/12-component-composition.md) for recommended
host boundaries, package boundaries, and extraction timing.
