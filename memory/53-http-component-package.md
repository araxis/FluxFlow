# HTTP Component Package

Date: 2026-06-01

## Decision

Add `FluxFlow.Components.Http` as a separate component package with one initial
node:

- `http.request`

The package is application-neutral. Hosts can either use the default per-node
sender or provide their own `IHttpRequestSenderFactory` for named clients,
shared clients, authentication, tracing, proxy configuration, or tests.

## Contracts

The first slice includes:

- `HttpRequestInput`
- `HttpResponseOutput`
- `HttpErrorOutput`
- `HttpErrorKind`
- `IHttpRequestSenderFactory`
- `IHttpRequestSender`

The sender abstraction receives a resolved `HttpRequestSendContext`, so hosts
do not need to duplicate URL, header, body, timeout, or body-limit resolution.

## Behavior

`http.request` consumes request inputs and emits typed response outputs.
Network, timeout, cancellation, invalid URL, body-size, and send failures are
emitted as `HttpErrorOutput` items on the `Errors` port. The node continues
processing later messages after per-message failures.

Non-success status codes never fault the node. They emit a response with
`Success = false`. If `treatNonSuccessStatusAsError` is enabled, the node also
emits a matching error item.

## Options

The node supports:

- `baseUrl`
- `defaultHeaders`
- `defaultTimeoutMilliseconds`
- `maxResponseBodyBytes`
- `followRedirects`
- `treatNonSuccessStatusAsError`
- `boundedCapacity`

## Verification

Focused coverage includes:

- sender replacement
- base URL and header resolution
- body and content type mapping
- success and non-success response routing
- optional non-success error routing
- invalid URL continuation
- timeout and cancellation errors
- default sender body-size limit enforcement
- diagnostics
- registration and option validation

Planned release tag:

```text
components-http-v0.1.0-alpha.1
```
