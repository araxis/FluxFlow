# Outbound HTTP client

Added FluxFlow.Components.Http with HttpClientComponent, HttpClientOptions and typed
HttpSendRequest/HttpResponse values. Configuration/Components/Http contains the resource
DTO, component DTO and AddHttp registrar. Core and Application implementation remain
unchanged. The resource is a native System.Net.Http.HttpClient registered in keyed DI;
there is no new connection manager or resource wrapper.

The component composes native TransformMany/Broadcast blocks with FlowLifetime and
the existing processing helper. Each input produces one buffered HTTP response with
status, multivalue headers and body. RequestUrl replaces the earlier Address property.
Request Body is FlowValue: structured JSON from a mapper is sent directly, while text
and Base64 remain supported. Explicit JSON null differs from an absent body. Native charset
decoding is retained. Responses, including 4xx/5xx, keep their status and body.
Transport, timeout, conversion and response-size failures use FlowError. Existing
input errors pass through. Derived results preserve message lineage.

Configuration registers factories and links without network work. DI owns the shared
client and handler. Each invocation owns and disposes its request and response.
Request headers do not mutate client defaults. Normal completion drains requests;
fault cancellation targets the component's work without canceling other users of the
shared client. Resource setup supports optional Microsoft.Extensions.Http.Resilience
retry, with unsafe methods excluded by default. Native retry owns status selection,
backoff and Retry-After handling; the component has no retry loop. Both resource and
per-request timeouts bound token acquisition, retries and complete response buffering.

Authentication supports Basic, static Bearer and OAuth client credentials. OAuth uses
Duende.AccessTokenManagement for caching, concurrent acquisition and expiration renewal.
A small handler applies tokens, honors explicit request Authorization, prevents sends
after failed acquisition and rechecks token expiration between retry attempts. Token
configuration is named by resource address; separate resources do not share credentials.
No token cache, renewal scheduler, connection wrapper or Core dependency was added.

The native handler uses a configured pooled connection lifetime, automatic response
decompression and OS TLS validation. Redirects are opt-in and cookies are not stored.
Client Timeout and MaxResponseContentBufferSize apply while buffering the complete
response, including responses without Content-Length. Text/Base64 projection allocates
additional memory. This is not a streaming, multipart or HTTP listener implementation.
Custom host-provided clients can supply additional native handlers and transport options.

## Verification

- 140 HTTP test executions passed on .NET 8/10, using local Kestrel HTTP servers and
  controlled native handlers. Coverage includes typed/JSON requests, text/binary
  bodies, charset conversion, status preservation, header isolation and snapshots,
  malformed input, transport errors without retries, timeout during body reading,
  response-size limits, cancellation, request/response disposal and DI ownership.
  Additional cases verify the JSONata-to-HTTP path, structured bodies, authentication
  overrides, retry budgets/method exclusions, replayed bodies, OAuth credential styles,
  expiry renewal, concurrent acquisition, resource isolation, acquisition failure and
  recovery, and deadlines during token acquisition and retry delay.
- Configuration integration verifies deferred network activity, source-to-client links,
  duplicate input/output declarations, shared clients, redirect visibility and atomic
  rejection of invalid resource settings.
- The public API baseline, package manifest, release dependency expectation and solution
  include the new module.
- Final full Release suite: 2,588 passing test executions, zero warnings.
- Isolated package consumer passed on .NET 8/10 with candidate version
  0.0.0-local.20261004193218, exercising the packaged registrar, OAuth and retry
  construction without network work, structured request values and native
  Input/Output/Events resolution. No package was published.

TLS is delegated to the standard HttpClient handler; these tests use local HTTP, not
external HTTPS services. OAuth is tested against a local protocol endpoint, not a live
identity provider. The API retry policy does not replay 401 responses or retry body
read failures after headers have arrived. Response bodies remain buffered text/Base64.
