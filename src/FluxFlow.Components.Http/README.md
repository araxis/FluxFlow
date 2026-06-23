# FluxFlow.Components.Http

A standalone HTTP node for FluxFlow — a "blockified" `HttpClient`.

## What it is

`HttpClientNode` is a self-contained TPL Dataflow processor. You give it an
`HttpClient`, post `HttpRequestInput`s to its input, and it broadcasts
`HttpResponseOutput`s on its output (failures on the error port, notes on the
event port). It needs **nothing else** — no engine, registry, or runtime:

```csharp
await using var node = new HttpClientNode(httpClient);

node.Output.LinkTo(logger.Input);   // broadcast: link the output to as many
node.Output.LinkTo(mapper.Input);   // downstream nodes as you like

await node.Input.SendAsync(new HttpRequestInput
{
    Method = "GET",
    Url = "https://api.example.com/things/42"
});
```

## Ports

| Port | Block | Purpose |
|------|-------|---------|
| `Input` | `BufferBlock<HttpRequestInput>` | bounded intake — `SendAsync` applies backpressure |
| `Output` | `BroadcastBlock<HttpResponseOutput>` | the response, fanned out to every linked consumer |
| `Errors` | `BroadcastBlock<FlowError>` | request failures (invalid URL, timeout, network, send, non-success) |
| `Events` | `BroadcastBlock<FlowEvent>` | `http.request.succeeded` / `http.request.failed` notes |

Outputs are broadcast (latest-wins, no backpressure): a consumer that keeps up
sees every message; one that falls badly behind may miss some. That is the
deliberate trade for simplicity. If a graph genuinely must not drop, bridge that
edge through its own bounded buffer.

## Transport policy lives on the HttpClient

The node owns no transport policy. Base address, connection pooling, redirects,
default headers, TLS, proxy, and any allow-list / SSRF guard all belong on the
`HttpClient` you inject — exactly as you configure a regular .NET client
(typically via `IHttpClientFactory` and a `DelegatingHandler`). The node never
disposes the client; the host owns its lifetime. A relative `Url` resolves
against the client's `BaseAddress`.

## Options

```csharp
new HttpClientNodeOptions
{
    BoundedCapacity = 128,            // input buffer size
    MaxResponseBodyBytes = 1_048_576, // bodies past this are read to the cap, BodyTruncated = true
    TreatNonSuccessStatusAsError = false,
    MaxDegreeOfParallelism = 1,       // >1 to send concurrently (output order not guaranteed)
    DefaultTimeoutMilliseconds = null // per-request timeout when the input omits one
};
```

## Composition

Building a workflow — reading config, creating nodes, linking them — is a
separate concern from the node. This package is just the node.

Add `FluxFlow.Components.Http.Composition` when a host wants to instantiate
`HttpClientNode` from `FluxFlow.Composition` fluent/config definitions. That
optional package registers the `http.client` factory and resolves a keyed
`HttpClient` resource named `client`; the host still owns the client lifetime
and transport policy.

The optional composition package also exposes
`HttpComponentDesignMetadataProvider` for neutral Designer metadata over the
`http.client` composition node type. The standalone HTTP package remains free
of Designer, Composition, and Engine dependencies.
