# vNext HTTP FlowContent And Results

Date: 2026-07-20

## Status

The twenty-seventh bounded vNext milestone is implemented on local branch
`work/http-vnext`. No push, tag, publication, pull request, or merge was
performed.

This milestone adds the canonical exact-content HTTP request/result surface and
makes it the default `http.client` Composition contract. Released direct-use
request/response types and node behavior remain available for compatibility.

## Canonical Runtime

- `HttpClientRequest` carries method, URL, defensively copied headers, optional
  exact `FlowContent` body, and optional semantic timeout.
- `HttpClientResult` implements `IFlowResult` and is JSON-polymorphic through
  `HttpResponseResult` and `HttpClientFailureResult`. `Kind`, `IsError`, stable
  `HttpErrorCodeNames`, and immutable FlowError details support ordinary
  workflow conditions and mapping.
- `HttpResponseResult` retains status, reason, immutable header values, bounded
  exact response content, status success classification, truncation, method,
  URL, elapsed time, and timestamp.
- Configured non-success handling returns `HttpClientFailureResult` while
  retaining the complete bounded response. By default non-2xx responses remain
  normal `HttpResponseResult` values with `Success=false`.
- `FlowContentHttpClientNode` exposes one typed Input, one polymorphic Output,
  Events, and no universal Errors port. Expected invalid request, timeout,
  cancellation, network, send, response-read, and configured non-success
  outcomes are ordinary results; later accepted inputs continue.
- Request content must have an original byte representation. Value-only content
  returns `http.invalid_content`, keeping serialization explicit upstream.
  Content type and encoding map to request headers; response bytes, full content
  type, and declared charset remain FlowContent metadata without hidden decode.
- Result envelopes preserve correlation, trace, and headers, create fresh
  message identity, and record request causation. Processing capacity and
  concurrency reuse validated `HttpClientNodeOptions`.
- The injected `HttpClient` and optional `TimeProvider` remain host-owned. The
  node neither creates nor disposes the client and does not own transport,
  authentication, retry, redirect, TLS, proxy, or endpoint policy.

## Composition And Designer

- Parameterless `RegisterHttpNodes()` now owns the canonical `http.client`
  descriptor with `HttpClientRequest` Input, `HttpClientResult` Output, Events,
  and no Errors surface.
- `RegisterHttpResponseOutput(nodeType)` preserves the released
  `HttpRequestInput` / `HttpResponseOutput` factory explicitly under a distinct
  type; that compatibility path retains legacy Errors and Events.
- The required keyed `HttpClient` and optional keyed `TimeProvider` resource
  shapes remain unchanged and host-owned.
- Designer metadata describes the canonical fixed ports and explains that
  configured non-success statuses become Output error results.
- Package examples use the flat `Resources` / `Workflows` document, direct
  resource addresses, and source-side links without implicit mapping or
  serialization.

## Compatibility And Versioning

- `FluxFlow.Components.Http` moves from local `3.0.3` to `4.0.0` for the
  additive canonical runtime surface. `HttpClientNode`, `HttpRequestInput`,
  `HttpResponseOutput`, old numeric error codes, decoded response text, and
  their released ports remain unchanged.
- `FluxFlow.Components.Http.Composition` moves from `1.4.0` to `2.0.0` because
  the default fixed input/output and Errors surfaces change.
- The latest public HTTP runtime package is `3.0.2`; local `3.0.3` was not
  published. SDK package validation passes for HTTP `4.0.0` against published
  `3.0.2` and HTTP Composition `2.0.0` against published `1.4.0`.
- Release preflight and complete package dry-runs pass for both packages using
  the seeded temporary current-package source outside tracked state.
- The source-declaration baseline records only intentional additive canonical
  declarations and the explicit compatibility registration. No released
  declaration was removed or signature-changed.

## Verification

- HTTP runtime tests: 25 passed.
- HTTP Composition tests: 15 passed.
- HTTP ASP.NET Core support tests: 16 passed.
- Core Composition tests: 126 passed.
- Composition Hosting tests: 38 passed.
- Designer tests: 98 passed.
- Release convention tests: 93 passed.
- The complete Release no-build sweep passed 2,119 tests across 63 projects
  with no failures or warnings.
- The first cold Debug build completed all 130 projects with zero errors and one
  transient warning hidden by ErrorsOnly; the immediate controlled rerun was
  clean with zero warnings. The first cold Release traversal exceeded its
  five-minute command window without compiler errors and left no FluxFlow build
  process; its warm controlled rerun completed all 130 projects with zero
  warnings and zero errors.
- A package-only net8 consumer restored HTTP `4.0.0` and HTTP Composition
  `2.0.0` into a fresh external cache, asserted canonical and compatibility
  descriptors, executed exact request/response FlowContent through the
  canonical node, verified message lineage, and printed
  `HTTP_PACKAGE_CONSUMER_OK`.

## Deferred Boundaries

- HTTP remains a request component over a host-owned client, not a client
  factory, endpoint registry, retry/authentication framework, or inbound HTTP
  trigger host.
- FlowContent transport does not imply automatic JSON/text decoding or
  encoding; Serialization and Mapping own those explicit conversions.
- Live Output remains broadcast fan-out, not durable response storage, polling,
  or a latest-value API.
- The ASP.NET Core trigger support package remains independent and unchanged.
- Legacy HTTP Composition `1.x` remains the stored-definition compatibility
  line.

## Next Gate

Assess FileSystem as the next bounded component-family pass. Migrate transported
file content to FlowContent and expected operations to typed normal results
while preserving base-path confinement, bounded reads, host ownership, and
released direct-use compatibility.
