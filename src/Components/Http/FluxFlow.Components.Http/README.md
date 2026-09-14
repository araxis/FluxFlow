# FluxFlow.Components.Http

Standalone workflow-value HTTP client node. It depends on Nodes, not Composition or
Engine.

`HttpClientNode` accepts and emits canonical `FlowMessage`. It owns
materialization from the public workflow shape to its private `HttpClientRequest`
and serializes `HttpResponseResult` back to `FlowValue`. Requests contain method,
URL, headers, optional exact `FlowContent` body, and optional timeout. Responses
preserve status, headers, exact bounded body, content metadata, truncation state,
and status classification.

Request parsing deserializes directly from `FlowValue` with a private body
converter. Text becomes UTF-8 text/plain content, exact FlowContent retains its
bytes and metadata, and other JSON becomes compact UTF-8 application/json content.
Missing or null bodies stay absent. The public HttpClientRequestMaterializer API
is unchanged; parsing no longer reconstructs the request as a mutable JSON tree.

```csharp
await using var node = new HttpClientNode(httpClient);
var results = new BufferBlock<FlowMessage>();
node.Output.LinkTo(results);

await node.Input.SendAsync(FlowMessage.Create<FlowValue>(((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new
{
    method = "POST",
    url = "v1/items",
    body = new { orderId = 42 }
}, new JsonSerializerOptions(JsonSerializerDefaults.Web))))));

var response = await results.ReceiveAsync();
if (!response.IsError)
    Console.WriteLine(response.Value.Json.GetProperty("statusCode"));
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

Install `FluxFlow.Components.Http.Composition` for optional component construction and registration. This runtime package remains free of Composition, Designer, and Engine dependencies.
