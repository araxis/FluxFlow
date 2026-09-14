# Integrated simplification review — 2026-09-13

The current solution builds successfully in Release with zero warnings and zero
errors. The configured local regression has no unresolved failures after reviewing
and updating its stale source API baseline. A separate in-process workflow
acceptance test and a minimal direct application example also pass.

## Fresh validation

Evidence directory: `artifacts/integrated-review-20260913`.

| Check | Result |
| --- | --- |
| Complete solution Release build | Passed, 0 warnings, 0 errors |
| Configured local regression, initial execution | 4,612 passed; 1 API-baseline failure; 4,613 total executions |
| API baseline verification after reviewed update, acceptance mode disabled | 2 passed, overlapping the initial suite |
| Separate in-process MQTT-to-HTTP workflow acceptance | 1 passed |
| Direct application/workflow example | Passed: invalid input becomes error, next input produces `HELLO!` |

The regression produced 76 TRX reports, 71 nonempty. Five suites were deliberately
filtered out by the repository's Unit filter: in-process acceptance (run separately),
broker/container workflow, external smoke and two licensed database integration
suites. Multiple target frameworks are counted as separate executions; these are
not counts of unique test methods. Do not add the overlapping two-test rerun to
the 4,613 regression total. The original failure report is preserved.

The initial API check found that `eng/public-api/baseline.txt` still described an
older package list and declarations. Generated a candidate without changing the
baseline, compared source declarations, reviewed public versus internal changes,
documented migration, then used the existing acceptance mechanism and reran with
that mechanism disabled. No runtime change was necessary in this review.

Existing previous-day Collector and package-consumer evidence remains separately
identified in the observability ledger; it was not rerun or counted as fresh
end-to-end coverage in this review. This is not full external-service, published
package-feed, benchmark or UI-consumer qualification.

## What is simpler now

- Ordinary applications directly own workflows and component instances. Workflow
  names and port addresses do not require the optional Composition/Engine layers.
- Payload-only transforms can implement one method. Message-aware transforms and
  general multi-output nodes remain available when their behavior is needed.
- FileSystem, Storage, Sessions, Metrics and Projections parsing moved into their
  component implementation; thin shared materializer classes were removed. Shape
  metadata remains available independently. Existing meaningful converters remain.
- Runtime data continues through the canonical FlowMessage/FlowValue boundary.
  Components convert locally to their own types; typed APIs elsewhere still exist.
- Shared processing boundaries handle ordinary exceptions, forwarding, tracing and
  metric scopes. Host adapters provide SDK/exporter setup and logging. Component
  business logic does not need an ActivitySource, logger or metric instrument.
- Definition naming replaces Authoring at the construction boundary. UI/editor
  source lives in the independent FluxFlow.UI project, outside this repository.

This simplifies the common development path. It does not mean all runtime
coordination can disappear: reliable bounded fanout, shutdown ordering, timers,
request/reply and aggregation still need explicit lifetime and ownership rules.
There is no evidence from this review that another general refactoring pass is
needed before using the current API.

## Minimal usage reviewed and executed

The actual 42-line verification program is retained at
`artifacts/integrated-review-20260913/example/Program.cs`. Its component body is:

```csharp
sealed class Uppercase : FlowValueTransformNode
{
    protected override ValueTask<FlowValue> ProcessAsync(
        FlowValue input, CancellationToken cancellationToken)
        => ValueTask.FromResult(
            FlowValue.From(input.Deserialize<string>()!.ToUpperInvariant()));
}
```

The ordinary graph construction is:

```csharp
await using var app = new FlowApplication();
var workflow = app.AddWorkflow("demo");
var upper = workflow.Add("uppercase", new Uppercase());
var suffix = workflow.Add("suffix", new Suffix());
upper.Output.LinkTo(suffix.Input);
await app.StartAsync();
var reply = await app.RequestAsync("demo.uppercase.Input", "demo.suffix.Output",
    FlowMessage.Create(FlowValue.From("hello")), TimeSpan.FromSeconds(5));
await app.StopAsync();
```

`Suffix` is another payload transform that appends `!`. Host registration is
`builder.Services.AddFluxFlowTelemetry()`. The test starts the host before the
application and stops the application before the host. Production hosted services
should preserve this ordering. SDK registration alone does not select an exporter;
see the [host guide](../46-observability-host.md) for exporter and SSE setup.

Subsequent user-directed request API update: the example above now omits the
former `RepliesFrom` prerequisite and uses port addresses. The original archived
verification program remains unchanged as historical evidence; current Core tests
verify the declaration-free, cross-workflow address API.

The check sent a JSON number first to exercise failed string conversion, then
`hello`. It verified an error reply followed by `HELLO!`, without adding error or
telemetry code to either component. Early harness mistakes (relative project path
and assuming Events belongs in the ordinary Outputs dictionary) were corrected;
they were not runtime defects.

## Size comparison and its limits

Baseline commit: `32c8e4817f238dcd9c51cfbaf22c82c1c52da24d` (the transferred,
already-refactored checkpoint). This is **not** the original pre-simplification
system. Counts are physical C# lines, including blanks/comments, from the baseline
Git archive versus current tracked plus untracked source, excluding ignored build
outputs. Files moved or renamed are included at their current locations.

| Scope | Files before → now | Lines before → now | Net lines |
| --- | ---: | ---: | ---: |
| Core | 129 → 133 | 12,591 → 12,813 | +222 |
| Components excluding UI | 417 → 417 | 32,671 → 32,581 | −90 |
| UI component source in this repository | 71 → 0 | 5,130 → 0 | −5,130 |
| Adapters | 2 → 6 | 187 → 463 | +276 |
| All production source | 780 → 717 | 75,665 → 70,943 | −4,722 |
| Tests | 455 → 457 | 119,013 → 105,361 | −13,652 |
| Samples | 45 → 30 | 3,664 → 2,519 | −1,145 |
| Engineering C# | 3 → 5 | 1,110 → 1,293 | +183 |

The production row includes Core, Components, Adapters and other production
folders; do not add those overlapping rows together. The 5,130 UI lines were
extracted, not eliminated from the wider product. Excluding that extraction,
production source is **408 lines larger** than the transferred checkpoint.
Observability adds capability and shared responsibility; these figures do not
support claiming a large further runtime code reduction since transfer.

Raw Git diff initially showed 42,127 deleted and 2,101 added lines across 570
tracked files, but omitted untracked replacements and mixed source, tests, project
files and documentation. It is unsuitable as the simplification savings total.
The reproducible measurement data is `source-counts.json` in the evidence directory.

## Compatibility and remaining scope

The reviewed source baseline includes additive telemetry APIs and intentionally
breaking prior changes: Definition naming, removed public thin materializers and
the MQTT publish node's base-type change. The source scanner also includes members
of internal/private helpers and file paths, so its hashes do not prove binary
compatibility. Details are in the [migration guide](../46-code-first-major-migration.md).
The affected packages remain an unpublished development preview; no release,
version promotion, commit, push or consumer migration was performed.

Standard OpenTelemetry and SSE meet the current observability scope. Connecting
Prometheus, Elasticsearch or another external backend is a host choice, not missing
framework work. Sessions remains explicitly deferred. The next useful input is
experience applying this simpler API to a real consumer or the user's separate
Sessions discussion; neither is automatically authorized by this local review.
