# Designer Renderer App First Slice

Date: 2026-07-03

## Summary

Started the Designer host layer renderer UI (docs/18 phase 5) as
`samples/FluxFlow.DesignerApp`, a Blazor WebAssembly + MudBlazor app over the
headless `FluxFlow.DesignerHost` model layer. First slice delivered and
verified in a real browser: component palette and option/resource inspector
driven end to end by the real package metadata catalog. Node canvas and
persistence remain follow-on slices.

## Decisions

- **Hosting model: Blazor WebAssembly, net10.0.** Honors the project's stated UI
  direction (WASM + MudBlazor + Z.Blazor.Diagrams) and matches
  `FluxFlow.DesignerHost` (net10.0). Verified WASM Debug builds in this
  environment without the `wasm-tools` workload (that workload is only needed
  for AOT publish, not for build/run).
- **MudBlazor 9.6.0**, current stable. `Z.Blazor.Diagrams 3.0.4.1` is the target
  for the canvas slice.
- **Catalog assembly** lives in `Features/Designer/DesignerCatalog.cs`, built
  once from a representative set of eight package metadata providers (Timers,
  Sources, Routing, Control, Validation, Http, Storage, Mqtt). Adding a family
  is a one-line provider addition; the palette showed 23 components across 8
  categories.

## Structure

Feature-first under `Features/Designer/`:
`DesignerCatalog.cs`, `Pages/DesignerPage.razor` (route `/`),
`Components/ComponentPalette.razor`, `Components/ComponentInspector.razor`,
`Components/OptionEditorField.razor`. MudBlazor shell in `Layout/MainLayout.razor`
with the standard providers; `Program.cs` registers `AddMudServices()` and the
`DesignerCatalog` singleton. Template junk (Counter/Weather/NavMenu/bootstrap)
was removed.

The inspector maps each `OptionEditorKind` to a MudBlazor control (text, number,
toggle switch, select, secret, multiline, expression, JSON), shows required
markers, advanced chips, helper text, section ordering, and the host-owned
resource picker prompts (picker kind, key pattern, value type, required).

## Verification

- `FluxFlow.DesignerApp` builds clean (Debug and as part of the full
  `FluxFlow.sln` Release build: `0` warnings, `0` errors).
- Ran the app on `http://localhost:5298` and inspected it in the browser via the
  preview tooling. Accessibility snapshot confirmed: palette renders 23
  components grouped into 8 categories; selecting `timer.interval` renders the
  inspector with `6 options · 1 resource`, sections in order (Diagnostics,
  Timing, Runtime), the required `Interval *` field, `Emit Immediately` as a
  toggle switch (Boolean -> Toggle), advanced chips, helper text, and the
  `Clock` resource prompt (picker kind `clock`, value type `TimeProvider`, key
  pattern `clock:{name}`). No console errors. (Screenshot capture timed out — a
  preview-tooling quirk with WASM — but structure and interaction were verified
  via snapshot and an eval-driven click.)
- Added the project to `FluxFlow.sln` and `docs/README.md`; release convention
  tests pass (`92` passed).
- `.claude/launch.json` added with a `designer-app` profile for the preview
  server.

## Boundaries and follow-on

- The app is a sample/tool, not a shipped package (no `PackageId`, excluded from
  the shared icon packaging by the `Directory.Build.targets` condition).
- Option editors currently hold local scratch values (no node instance yet);
  value binding to a graph node arrives with the canvas slice.
- Follow-on slices: node canvas with Z.Blazor.Diagrams (add-from-palette,
  select-to-inspect, ports/links from `GraphModel`); persistence save/load to
  `CompositionDefinition` JSON via `GraphDefinitionMapper`; validation display
  via `ValidationMessageModel`.
