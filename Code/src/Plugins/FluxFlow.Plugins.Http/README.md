# HTTP plugin

The package installs HTTP Client, HTTP Trigger and the HTTP connection. Studio Run starts a loopback HTTP listener when the application contains HTTP Trigger. Each application instance gets an available port; the original `Microsoft.Hosting.Lifetime` log entry, `Now listening on: ...`, shows its address in the run logs. Append the configured Route to that address. Stop closes the listener; rerun allocates a new listener. Installing the package alone does not open a listener.

The listener shares the workflow's service provider and lifecycle. It is separate from the companion management API and listens only on `127.0.0.1`. No URL or port is written into the saved project. For a fixed public endpoint, TLS or authentication, use the existing [ASP.NET Core host integration](Hosting/FluxFlow.Hosting.AspNetCore/README.md).

An environment can bind an HTTP connection to an existing endpoint or to WireMock.NET. A container-enabled companion creates the mock at Run, loads the saved mappings and removes the container at Stop. The mock keeps the configured base path; it does not receive the original service authentication or default Authorization header. Locked profiles remain read-only.

## HTTP client setup

The resource owns the shared client: base URL, authentication, retry and timeout.
The component receives an `HttpSendRequest` through Input and emits an `HttpResponse`
through Output. JSONata can build the request directly; no separate request/reply
ports or fixed request template are needed.

```csharp
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Expressions.Jsonata;

services.AddHttp().AddMapper().AddJsonataExpressionEngine();
services.AddFluxFlowApplication(configuration);
```

```json
{
  "Resources": {
    "api": {
      "Type": "http.client",
      "BaseAddress": "https://api.example.com/v1/",
      "Timeout": "00:00:30",
      "Headers": { "Accept": "application/json" },
      "Authentication": { "Scheme": "Bearer", "Token": "replace-through-host-configuration" },
      "Retry": {
        "MaxRetryAttempts": 3,
        "Delay": "00:00:02",
        "BackoffType": "Exponential",
        "UseJitter": true,
        "MaxDelay": "00:00:30",
        "RetryUnsafeMethods": false
      }
    }
  },
  "Workflows": {
    "orders": {
      "send": { "Type": "http.client", "Client": "api", "Input": "request.Output" },
      "request": {
        "Type": "mapper",
        "Engine": "jsonata",
        "Expression": "{ 'Method': 'POST', 'RequestUrl': 'orders', 'Headers': { 'X-Source': 'production-line' }, 'Body': { 'quantity': 2, 'active': true }, 'Timeout': '00:00:10' }"
      }
    }
  }
}
```

Connect a trigger or scheduler output to `orders.request.Input` to drive this flow.
Client selects the resource; it does not describe the request. Mapper expressions can
take the URL, headers and body from incoming data. Header values are strings; body
values retain their JSON types. Input/Output/Events/Error references and OutputCapacity
follow the existing component configuration contract. Multiple components can share
one client through the standard workflow-local/global resource lookup.

## Authentication

Choose one authentication configuration on the resource:

```json
{ "Scheme": "Basic", "Username": "service-user", "Password": "replace-through-host-configuration" }
```

```json
{ "Scheme": "Bearer", "Token": "replace-through-host-configuration" }
```

```json
{
  "Scheme": "OAuthClientCredentials",
  "TokenEndpoint": "https://identity.example.com/connect/token",
  "ClientId": "orders-client",
  "ClientSecret": "replace-through-host-configuration",
  "Scope": "orders.write",
  "ClientCredentialStyle": "AuthorizationHeader"
}
```

OAuth uses Duende.AccessTokenManagement for token acquisition, concurrent request
coordination, caching and renewal before expiration. Tokens are obtained on demand
when sending; registration and application construction perform no network I/O.
ClientCredentialStyle also accepts PostBody. Scope is optional. The token endpoint
requires HTTPS, with HTTP allowed on loopback for development. Token requests do not
follow redirects. The resource timeout also bounds token acquisition.

Failed token acquisition stops the API request and becomes FlowError. Token protocol
failures are not retried by the API status-code retry policy. Each HTTP retry checks
the managed token again. A 401 remains a response; it does not automatically replay
the request. Renewal obtains a new client-credentials token, not a refresh token.

Per-request Authorization overrides resource authentication, including OAuth.
Other per-request headers also override defaults without changing the shared client.
Do not specify both Authentication and a default Authorization header. API keys can
use Headers. Host configuration providers supply secrets; URL credentials are rejected.

## Retry and timeout

Retry is optional. Omit it or use MaxRetryAttempts = 0 for one attempt. When configured,
Microsoft.Extensions.Http.Resilience supplies HTTP retry behavior: transport errors,
408, 429 and 5xx responses. MaxRetryAttempts counts retries after the initial attempt.
Defaults are three retries, exponential backoff, two-second initial delay, jitter and
30-second maximum delay. ShouldRetryAfterHeader defaults to true and honors server
Retry-After values using the native policy.

POST, PATCH, PUT, DELETE and CONNECT are excluded unless RetryUnsafeMethods is true.
Enable that only when replaying the operation is appropriate. Each retry sends the
same buffered request body. The last HTTP response, including an exhausted 503, keeps
its body and status. Response-body read failures are not retried after response headers
have been received. No circuit breaker or second application retry loop is added.

Resource Timeout defaults to 100 seconds and bounds the whole operation, including
OAuth acquisition, retry delays and response-body buffering. An optional request
Timeout can shorten that deadline without mutating the shared client. Both must be
positive. MaxResponseContentBufferSize defaults to 10 MiB (1 to Int32.MaxValue bytes),
including bodies without Content-Length. Text/Base64 projections allocate additional
memory beyond that buffer.

## Native client lifetime

AddHttp registers http.client as both a resource and component type. The resource is
a native System.Net.Http.HttpClient. DI owns it and its handlers; components borrow it.
BaseAddress is optional for absolute request URLs. PooledConnectionLifetime defaults
to two minutes. Native SocketsHttpHandler owns connection pooling and DNS refresh.
GZip, Deflate and Brotli decompression are enabled, cookies are not stored, redirects
default to false, and TLS uses normal OS trust and hostname verification.

Hosts needing client certificates, proxies or custom handlers can register a native
HttpClient through AddFluxFlowResource or AddExternalFluxFlowResource and use the same
Client reference. No Core or Application dependency on HTTP authentication is added.

## Hosted integration scenario

The [HTTP hosting configuration](../../../tests/Unit/FluxFlow.Components.Http.Tests/HttpHosting.json)
and [hosted tests](../../../tests/Unit/FluxFlow.Components.Http.Tests/HttpHostingTests.cs)
exercise Scheduler → JSONata request mapper → HTTP Client → JSONata log mapper → Logger
through the existing Generic Host. The tests reuse the local Kestrel server and override
only the resource address and timeout through standard configuration providers. A test
TimeProvider advances the scheduler without wall-clock sleeps; HTTP uses real loopback I/O.

The scenario checks dynamic request paths, headers and JSON bodies, resource authentication,
response lineage, structured application logs, duplicate link declarations, transport
failure, timeout, recovery, and draining an in-flight request before DI disposal.
HTTP 503 remains a response and the log mapper selects Error severity. Transport failures
and timeouts follow the configured Error reference unchanged. Logger forwards those error
messages without writing a log; it requires a LogRequest to perform logging.

These cases run on .NET 8 and 10 in the existing HTTP test project and CI test step:

```powershell
dotnet test tests/Unit/FluxFlow.Components.Http.Tests/FluxFlow.Components.Http.Tests.csproj -c Release --filter FullyQualifiedName~Hosted_configuration
```

No external API, broker, collector or additional hosting abstraction is required.
Input/link observation and actual Windows Service verification remain pending.

See [request and response values](Runtime/FluxFlow.Components.Http/README.md),
[Microsoft HTTP resilience](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience)
and [Duende token management](https://docs.duendesoftware.com/accesstokenmanagement/).
