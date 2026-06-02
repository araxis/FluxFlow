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

Completed local verification:

- `dotnet test tests\FluxFlow.Components.Http.Tests\FluxFlow.Components.Http.Tests.csproj -c Release --no-restore`
  passed with 14 tests.
- `dotnet build FluxFlow.sln -c Release --no-restore` passed with 0 warnings.
- `dotnet test FluxFlow.sln -c Release --no-restore` passed.
- `dotnet pack src\FluxFlow.Components.Http\FluxFlow.Components.Http.csproj -c Release --no-build --no-restore /nr:false -o artifacts\packages`
  created the package and symbol package.
- Commit: `32a0265` (`Add deterministic http clock`).
- Release-notes fix commit: `da57226` (`Add http release notes`).
- Tag: `components-http-v0.2.0-alpha.1`.
- Initial release workflow `26840774581` stopped before publish because the
  root changelog was missing the package section.
- Release workflow `26840973783`, success.
- Main CI workflow `26840963094`, success.
- Public package restore/build smoke passed on attempt 8 after public-feed
  indexing caught up.
