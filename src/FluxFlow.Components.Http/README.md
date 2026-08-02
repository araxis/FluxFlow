# FluxFlow.Components.Http

Standalone typed HTTP client node. It depends on Nodes, not Composition or
Engine.

`HttpClientNode` accepts `HttpClientRequest` and emits `HttpResponseResult`.
Requests contain method, URL, headers, optional exact `FlowContent` body, and
optional timeout. Responses preserve status, headers, exact bounded body,
content metadata, truncation state, and status classification.

```csharp
await using var node = new HttpClientNode(httpClient);
var results = new BufferBlock<FlowMessage<HttpResponseResult>>();
node.Output.LinkTo(results);

await node.Input.SendAsync(FlowMessage.Create(new HttpClientRequest
{
    Method = "POST",
    Url = "v1/items",
    Body = FlowContent.FromBytes(bytes, "application/json", "utf-8")
}));

var response = await results.ReceiveAsync();
if (!response.IsError)
    Console.WriteLine(response.Value.StatusCode);
```

Invalid requests, timeout, network/send/read failure, and configured non-success
handling become `FlowError` on Output. There is no separate failure result or
Errors port. Non-success responses remain normal `HttpResponseResult` values by
default; `TreatNonSuccessStatusAsError` changes that policy.

The host supplies and owns `HttpClient`, including base address, pooling,
authentication, TLS, redirects, proxy, retry, and endpoint policy. The node
never disposes it. Response decoding is explicit downstream; the HTTP node
preserves bytes and declared content metadata.

## Composition

Install `FluxFlow.Components.Http.Composition` for optional FluxFlow.Composition factories and Designer metadata. This runtime package remains free of Composition, Designer, and Engine dependencies.
