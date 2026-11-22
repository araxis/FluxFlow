# FluxFlow.Components.Http

`HttpTrigger` publishes incoming request messages and optionally awaits correlated
messages on `Response`. It does not own a server. ASP.NET Core registration, request
snapshots and endpoint mapping are supplied by the optional
[hosting integration](../../Hosting/FluxFlow.Hosting.AspNetCore/README.md).
`HttpReply` describes workflow-produced status, body, headers and content type.
`HttpTrigger` defaults to `HttpTriggerShutdownMode.Cancel`. With `Drain`,
`StopProduction()` refuses new requests and waits for accepted operations to finish
before stopping its lifetime. Reply deadlines, client cancellation and component
faults still release pending waits. This uses the existing `FlowLifetime` and
request/reply coordination; the component does not own a host or shutdown timer.

`HttpClientComponent` receives one `HttpSendRequest`, sends it through a shared native
`System.Net.Http.HttpClient`, and emits one `HttpResponse`. Input can be a typed
FlowValue or JSON produced by a mapper. The module depends only on Core and .NET.

```json
{
  "Method": "POST",
  "RequestUrl": "orders",
  "Headers": { "Authorization": "Bearer token" },
  "Body": { "quantity": 2 },
  "Timeout": "00:00:10"
}
```

Method defaults to GET. RequestUrl is absolute HTTP(S), or relative to the shared
client's BaseAddress. Native URI resolution applies: a base ending in `/api/` plus
`orders` gives `/api/orders`; `/orders` starts at the host root. Embedded URI
credentials are rejected. Authentication belongs in the resource or request headers.
Request headers override native client defaults for the same name. Content headers
require a body. ContentType requires Body, which can be an empty string.
Content-Type can also be supplied in Headers; specify it there or through ContentType,
not both. Either form controls the charset used to encode a text body.

Body is an optional FlowValue. Objects, arrays, numbers, booleans and explicit JSON null
are sent as JSON with application/json by default. An omitted Body sends no content.
Strings are sent as text/plain with UTF-8 charset by default; JSONata can pass an
object directly without `$string(...)`. Encoding = Json also allows a JSON string
literal. For binary input, use Encoding = Base64 and a Base64 string body. ContentType
can override the media type and charset, including for form-encoded text.

For application/x-www-form-urlencoded, Body must already be an encoded string.
For multipart fields and file uploads, use Form instead of Body:

```json
{
  "Method": "POST",
  "RequestUrl": "upload",
  "Form": {
    "Fields": { "deviceId": "robot-01", "description": "Camera capture" },
    "Files": [
      {
        "Name": "image",
        "FilePath": "C:/captures/frame.jpg",
        "FileName": "frame.jpg",
        "ContentType": "image/jpeg"
      }
    ]
  }
}
```

Form uses native .NET MultipartFormDataContent. Fields are string values; Files is an
array of HttpFormFile values. Either collection can be omitted, but at least one field
or file is required. Files may share a Name to upload several files under one field.
FileName defaults to the local basename; each file's ContentType defaults to
application/octet-stream. FilePath refers to the runner machine, with relative paths
resolved against its process working directory. Creating or serializing a request does
not open files; they open when that request is processed.

Body and Form are mutually exclusive, including an explicit JSON null Body. For Form,
omit request-level ContentType and the Content-Type header: .NET supplies the multipart
boundary. Encoding applies only to Body and must remain at its default for Form.
File parts stream from disk using StreamContent. The request owns and closes every
opened file on success, failure, timeout or cancellation, including partial form setup.
No entire-file byte array or Base64 conversion is required for these uploads.

Uploads use the shared HTTP resource's existing retry policy. Native StreamContent
rewinds the seekable file streams for subsequent attempts. POST uploads are not retried
unless RetryUnsafeMethods is enabled on that resource; enabling it can repeat a server
operation. Files must remain available and unchanged for the duration of the request.

```json
{
  "StatusCode": 200,
  "ReasonPhrase": "OK",
  "Headers": { "Content-Type": ["application/json; charset=utf-8"] },
  "Body": "{\"id\":42}",
  "Encoding": "Text"
}
```

ResponseEncoding defaults to Text. Native .NET decoding honors the response charset.
For binary data, request `"ResponseEncoding": "Base64"` to preserve every byte.
Headers retain multiple values, including content headers and received trailers.
The text body remains a string: a downstream JSONata mapper can use `$eval(Body)`
when the API returns JSON. Empty responses have an empty body.

All received status codes, including 4xx/5xx, remain ordinary response values.
Network errors, timeouts, malformed requests and oversized responses become FlowError
through the standard processing path. Existing input errors pass through unchanged.
Results retain the input's trace, correlation, headers and causation relationship.

Request and response JSON serialization uses a component-local System.Text.Json generated
context over the existing contracts. Custom Body values retain their own FlowValue JSON
representation. Typed JSON uses declared C# property names, including `StatusCode` and
`RequestUrl`; body properties and dictionary keys are preserved. JSON inputs remain
case-insensitive. JSON request deserialization still uses the native reflection-based reader:
on the supported targets, generated deserialization overwrites omitted init-only defaults
such as Method and Headers. This preserves existing request behavior; the JSON-input path
is not reflection-free.

## Lifetime

Create the component with `new HttpClientOptions { Client = sharedClient }`.
The component borrows the client; the host or DI owns its disposal. Each request and
native response is disposed after the response value has been copied. Input is processed
sequentially through a bounded TransformManyBlock; Output and Events use standard
broadcast blocks and FlowLifetime. OutputCapacity defaults to 128.

Completion drains admitted requests; faults cancel outstanding work through the
component's stopping token. Client Timeout bounds sending and reading the response
body, including resource authentication and retry. Request Timeout optionally sets
a shorter total deadline. Client MaxResponseContentBufferSize bounds the buffered
response. This version returns buffered responses, not streams. Authentication and
retry are configured on the shared resource; the component itself has no retry loop.

Native HttpClient instrumentation participates in the current processing activity;
no telemetry exporter or SDK dependency is added. Normal framework diagnostics use
the existing Events/application observation path.

See [configuration setup](../../README.md).
