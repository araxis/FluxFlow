# UI extracted into an independent package consumer

Location update: the user subsequently requested a sibling project folder.
The UI project now lives at `C:\Projects\FluxFlow.UI`, entirely outside the
FluxFlow repository folder. All 206 source/configuration file hashes matched
after the move, all project references resolved within the new folder, and the
independent build plus all 492 UI behavioral tests passed there. The original
`C:\Projects\FluxFlow\FluxFlow.UI` folder no longer exists. Paths in the original
extraction record below describe the initial staging location.

The user requested that FluxFlow contain only workflow definition/execution and
that all editor/UI code belong to a separate project consuming FluxFlow packages.
This supersedes the earlier decision to retain optional Designer packages in the
main distribution. The preceding Authoring-to-Definition rename is preserved.

## Result

- `FluxFlow.sln` and `eng/packages.json` contain 61 library packages, no Designer
  projects, editor samples or rendering dependencies.
- `FluxFlow.UI/FluxFlow.UI.sln` is a separate build with its own SDK/build/package
  configuration, license, package manifest, assets and README. It can be moved to
  another repository; no UI project reference points outside that folder.
- The shared Designer package and 19 family Designer packages now belong to UI.
  Their existing package/namespace identities are retained. UI-to-runtime edges
  are NuGet PackageReference entries, not ProjectReference entries or source links.
- 71 library C# files (5,130 physical lines, including comments/blanks) moved out
  of the runtime source tree. The 33 editor/host sample files moved as well.
- Mixed component tests were split by responsibility. Eight test projects became
  empty after their cases moved to UI and were removed from the main solution.
  Runtime fixtures no longer depend on Designer. UI has its own test fixtures.
- Source-level cross-layer audits are explicitly separate from the normal UI
  solution and require a matching source checkout through `FLUXFLOW_SOURCE_ROOT`.
  Normal UI build and behavioral tests consume packages only.

Structural port facts, required options/resources, document parsing, validation,
component execution, lifecycle, durability and telemetry remain in FluxFlow.
Display labels/icons, palette/inspector hints, visual editing/persistence models,
headless editor projections and the Blazor renderer belong to FluxFlow.UI.

## Verification on this machine

- Main Release solution build: zero errors. NuGet audit lookup warnings report
  that `https://api.nuget.org/v3/index.json` was unavailable; these were not suppressed.
- Main configured local regression: **4,332 passed, zero failed**, 66 nonempty
  suite summaries. Existing opt-in acceptance/container/external suites remain
  outside this command.
- UI Release build against local runtime packages: zero errors; the same NuGet
  audit availability warnings apply.
- UI behavioral suites: **492 passed, zero failed** (181 shared Designer,
  23 host-model, 288 component/presentation cases).
- Optional source-contract audits: **46 passed, zero failed**, with the matching
  source checkout explicitly supplied.
- All UI project references resolve within the UI folder. No runtime production
  C# or project file references a Designer package. The new runtime distribution
  boundary test prevents reintroducing editor packages/rendering dependencies.
- The current 61-package public declaration baseline was regenerated and checked.
  Historical reports, recovery stash and earlier feed identities remain preserved.

`eng/prepare-ui-feed.ps1` produced a fresh development feed at
`artifacts/ui-boundary-20260912/feed`, version
`0.0.0-local.20260912.ui-boundary.1`. It contains 61 package archives and their
symbols. This package-backed UI build is not publication, real-service conformance
or FluxMQ-NG acceptance. Subsequent documentation refinements are source changes;
the feed is immutable and must receive a fresh version before release preparation.

Local command logs are named `fluxflow-ui-*` in the machine's temporary directory.
No security policy, SDK/runtime installation, external service, publication, tag,
push, merge or external project was changed. Changes remain in the working tree.
