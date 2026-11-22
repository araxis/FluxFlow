# FluxFlow.Hosting.AspNetCore

Maps configured `http.trigger` components into an existing ASP.NET Core application or route group.
The host owns Kestrel, TLS, middleware, authorization policies and its single service provider.
The HTTP component library remains independent of ASP.NET Core.

For a complete runnable flow with a JSONata Mapper, see the
[configuration-driven HTTP sample](../../../../../samples/Hosting/FluxFlow.Hosting.Http.Sample/README.md).

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpTriggers()
    .AddFluxFlowApplication(builder.Configuration.GetSection("Flow"))
    .AddFluxFlow();
var app = builder.Build();
app.MapGroup("/automation").MapFluxFlowHttpTriggers();
await app.RunAsync();
```

Use namespaces `FluxFlow.Application`, `FluxFlow.Configuration.Application` and
`FluxFlow.Hosting.AspNetCore`. Call registration before `Build`, and map once before
starting the host. Mapping returns a native route group: host policies, endpoint
filters and conventions can be applied normally. Authentication services/middleware
must be configured by the host when requiring authorization.

```json
{
  "Workflows": {
    "api": {
      "receive": {
        "Type": "http.trigger",
        "Route": "/items/{id:int}",
        "Methods": ["POST"],
        "ResponseMode": "Reply",
        "ReplyTimeout": "00:00:30",
        "MaxPendingRequests": 1024,
        "MaxRequestBodyBytes": 1048576,
        "Output": "process.Input",
        "Response": "process.Output"
      }
    }
  }
}
```

The example requires a `process` component that produces reply values. Standard
`Output`, `Events` and `Error` links use existing port references; `Response` accepts
one or many reply sources. Reply links retain message correlation. No physical Error
port is created. Route constraints and matching are native ASP.NET Core behavior.

`ResponseMode` defaults to `Acknowledge`: publication returns the configured
`AcknowledgementStatusCode` (default 202). It does not guarantee durable delivery or
downstream completion. Response links require `Reply` mode. Reply mode publishes with
a fresh correlation and waits for the first matching response; duplicates and late
replies are ignored. Timeout returns 504 and workflow error replies return 500.
`ShutdownMode` defaults to `Cancel`: stopping the workflow or host cancels pending
waits with 503 where the connection remains available. Set `"ShutdownMode": "Drain"`
on the trigger to reject new requests with 503 while allowing already accepted
requests to finish. Their existing `ReplyTimeout` and client cancellation still
apply. If the host starts shutting down while it is still reading a request body,
the request is not published; a connected client receives 503.
Canceling the workflow stop token faults the run and cancels remaining waits.
The host's normal shutdown deadline also remains in effect. No extra drain timeout
is introduced. Acknowledgement mode waits only for publication, not downstream work.
These settings apply to both HTTP/1.1 and HTTP/2.
Exhausted pending-request capacity also returns 503 without publishing another request.
Client disconnection cancels its wait, not already published downstream work.

Incoming values contain `Method`, `Path`, `ContentType`, `Headers`, `Query`,
`RouteValues`, `Body` and `Encoding`. Headers/query retain arrays of values. JSON
bodies become JSON values; other UTF-8 bodies become text, with invalid UTF-8 encoded
as Base64. Empty bodies are null. Request size is bounded even without Content-Length;
the default is 1 MiB. Invalid JSON returns 400 and oversized bodies return 413.
These are owned snapshots; HttpContext and request streams never enter the workflow.

Form requests (`application/x-www-form-urlencoded` and `multipart/form-data`) also
include `Form` and `Files`, parsed by ASP.NET Core's `ReadFormAsync`. For example:

```json
{
  "Form": { "tag": ["one", "two"], "description": ["reading"] },
  "Files": [
    {
      "Name": "upload",
      "FileName": "reading.txt",
      "ContentType": "text/plain",
      "Length": 5,
      "Content": "aGVsbG8="
    }
  ]
}
```

Fields always contain arrays, including single and empty values. Files retain their
order and repeated field names; `Content` is always Base64, including text files.
`Length` is the original byte count. Both collections are empty when a form has no
corresponding entries, and omitted for non-form requests. The existing `Body` and
`Encoding` remain available with their original raw-body behavior.

Files are copied into the message before publication: no `IFormFile`, stream or
temporary path escapes the request. The snapshot remains usable after the request
and host end. This is bounded, buffered upload handling, not large-file streaming;
raw body and parsed file content both occupy space in the message. `MaxRequestBodyBytes`
limits the entire incoming body, including multipart boundaries and headers, before
parsing. ASP.NET Core's native form limits also apply; hosts can configure `FormOptions`
through standard DI. Malformed/truncated forms and native parser-limit violations
return 400 without publication; the trigger's total body limit returns 413.
Native buffering owns any temporary files and cleans them up at request completion.
File names and content types are client-supplied metadata, not trusted storage paths.

JSONata expressions can read `Form.tag[0]`, `Files[0].FileName` and `Files[0].Content`
directly. No additional component configuration or upload resource is needed.

Reply values have `StatusCode` (default 200), optional `Body`, `Headers` (string values)
and `ContentType`. For example `{ "StatusCode": 201, "Body": { "accepted": true } }`.
Typed components can use `HttpReply`. Text bodies are written as text; other
values as JSON. ASP.NET Core owns response framing, so Content-Length and
Transfer-Encoding are not workflow-configurable. Body is suppressed for 204/304.
An explicitly supplied content type is retained even when the reply has no body.

The adapter uses ordinary DI mapping delegates and the existing FluxFlow hosted
service. It does not build another server, provider, route registry or request/reply
coordinator. The same host can use standard Windows Service integration; elevated
service installation verification remains separate.
