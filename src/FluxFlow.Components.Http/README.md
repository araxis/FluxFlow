# FluxFlow.Components.Http

Standalone HTTP nodes for FluxFlow. The canonical node transports exact
`FlowContent` bodies and emits one polymorphic result stream. It requires only
TPL Dataflow through `FluxFlow.Nodes`; it does not require Composition, Engine,
hosting, reflection, or assembly scanning.

## Canonical Contract

`HttpClientNode` has one typed request Input, one result Output, and
Events for diagnostics:

| Port | Type | Purpose |
|------|------|---------|
| `Input` | `FlowMessage<HttpClientRequest>` | HTTP method, URL, headers, optional exact body, and optional timeout. |
| `Output` | `FlowMessage<HttpClientResult>` | `HttpResponseResult` or `HttpClientFailureResult`. |
| `Events` | `FlowEvent` | Best-effort request completion/failure diagnostics. |

There is no universal Errors port. Expected invalid-request, timeout, network,
send, response-read, and configured non-success outcomes are normal
`HttpClientFailureResult` values. `IsError`, `Kind`, `Error.Code`, and immutable
`Error.Details` make those outcomes directly selectable by conditions and
mappers. Unexpected pipeline faults remain observable through `Completion` and
host runtime system streams.

```csharp
var body = FlowContent.FromBytes(
    Encoding.UTF8.GetBytes("{\"name\":\"sample\"}"),
    "application/json",
    "utf-8");

await using var node = new HttpClientNode(httpClient);
var results = new BufferBlock<FlowMessage<HttpClientResult>>();
node.Output.LinkTo(results);

var request = FlowMessage.Create(new HttpClientRequest
{
    Method = "POST",
    Url = "v1/items",
    Headers = new Dictionary<string, string>
    {
        ["X-Request-Source"] = "workflow"
    },
    Body = body,
    Timeout = TimeSpan.FromSeconds(30)
});

await node.Input.SendAsync(request);

var response = await results.ReceiveAsync();
if (response.Payload is HttpResponseResult completed)
{
    Console.WriteLine(completed.StatusCode);
}
else if (response.Payload is HttpClientFailureResult failed)
{
    Console.WriteLine($"{failed.Error!.Code}: {failed.Error.Message}");
}
```

The output envelope preserves correlation, trace, and headers, creates a fresh
message id, and records the request message id as causation.

## Content Boundary

Request bodies must have `FlowContent.HasOriginalRepresentation == true`.
HTTP sends those bytes exactly and uses `FlowContent.ContentType` and
`FlowContent.Encoding` for content headers. A value-only body returns
`http.invalid_content`; serialize it explicitly upstream with a Serialization
component. The HTTP node does not silently choose JSON, text, or another codec.

Every received response body is captured as `FlowContent`, including empty and
binary bodies. `MaxResponseBodyBytes` bounds capture and `BodyTruncated` reports
truncation. Content type and declared charset remain metadata for downstream
inspection or decoding; the canonical node does not decode the body.

By default every received HTTP response, including non-2xx status, is an
`HttpResponseResult`; its `Success` property reports the status classification.
Set `TreatNonSuccessStatusAsError` to return `HttpClientFailureResult` instead.
That failure retains the complete bounded response in `Response`.

## Transport Ownership

The host supplies and owns `HttpClient`. Base address, connection pooling,
redirects, default headers, authentication, TLS, proxy, retries, and endpoint
allow-list policy belong on that client and its handlers. The node neither
creates nor disposes it. Relative URLs resolve against `HttpClient.BaseAddress`.

The optional `TimeProvider` controls result and event timestamps. Per-request
timeouts come from `HttpClientRequest.Timeout`, then
`HttpClientNodeOptions.DefaultTimeoutMilliseconds`, then the injected
`HttpClient` policy.

## Processing Options

```csharp
new HttpClientNodeOptions
{
    BoundedCapacity = 128,
    MaxResponseBodyBytes = 1_048_576,
    TreatNonSuccessStatusAsError = false,
    MaxDegreeOfParallelism = 1,
    DefaultTimeoutMilliseconds = null
};
```

All positive numeric settings fail fast when invalid. A parallelism of one
preserves request/result order; higher parallelism allows concurrent sends and
completion-order output.

## Composition

Install `FluxFlow.Components.Http.Composition` for canonical `http.request`
registration and Designer metadata. The runtime package remains free of
Composition, Designer, Hosting, and Engine dependencies.
