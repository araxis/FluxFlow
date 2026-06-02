# HTTP Clock Hardening

Date: 2026-06-02

## Decision

Harden `FluxFlow.Components.Http` with a host-provided clock for request output
timing.

HTTP responses and errors are often consumed by assertions, dashboards, logs,
and retry logic. The package should keep the default system behavior for
existing callers while allowing deterministic timestamps and elapsed
milliseconds in hosts and tests.

## Package Shape

- Package: `FluxFlow.Components.Http`
- Version: `0.2.0-alpha.1`
- Clock contract: `IHttpClock`
- Default clock: `SystemHttpClock`
- Registration: `RegisterHttpComponents(options => options.UseClock(clock))`

## Behavior

- `http.request` captures start and completion timestamps from the configured
  clock.
- `HttpResponseOutput.Timestamp` and `ElapsedMilliseconds` are assigned by the
  node, even when a custom sender returns different timing values.
- `HttpErrorOutput.Timestamp` and `ElapsedMilliseconds` are assigned by the
  node for invalid requests, timeouts, cancellations, network failures, body
  size failures, and optional non-success status errors.
- `HttpRequestSenderContext` exposes the configured clock to host-provided
  sender factories.
- Existing `RegisterHttpComponents()` callers keep the default system clock.

## Verification

Initial local verification:

- `dotnet test tests\FluxFlow.Components.Http.Tests\FluxFlow.Components.Http.Tests.csproj -c Release --no-restore`
  passed with 14 tests.
