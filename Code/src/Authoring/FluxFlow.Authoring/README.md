# FluxFlow.Authoring

UI-independent configuration authoring for command-line applications, tool hosts
and Studio. See [the authoring guide](../README.md) for ownership and dependencies.

- IComponentDescription supplies palette, port and inspector metadata through ordinary DI.
  The built-in descriptions live here in `Definitions`, without Razor or MudBlazor dependencies.
  Headless hosts use `AddDesignerComponents()` for built-ins or `AddCatalogComponents(catalog)`
  for built-ins plus installed plugins. MQTT and RabbitMQ supply their definitions, connection
  defaults and documentation through the catalog.
- DesignerConfiguration preserves the original JSON while editing known nodes
  and specific link declarations. It is editor state, not a runtime definition model.
- DesignerConfigurationValidator invokes existing component registration delegates
  and returns located ValidationMessageModel results without building a provider
  or activating components. Unsupported dependencies are reported as not validated.
- Palette, inspector and diagnostic models contain no Razor or platform services.

Canvas layout, file dialogs and rendering belong to the UI. Runtime execution,
resource resolution and expression compilation belong to the runner.

The existing Studio.Tests project verifies configuration preservation and
registration checks on .NET 8 and 10.

## Discovery and workflow inspection

Register descriptions with `AddDesignerComponents()` from
`FluxFlow.Authoring.Definitions`, then resolve `IEnumerable<IComponentDescription>`
through normal DI. Studio uses the same registration and description classes. Custom
descriptions use `AddSingleton<IComponentDescription, MyDescription>()`; no additional
catalog or scanning is involved. Register one description per component type.
These are design-time capabilities, not a claim that a particular runner has installed
the corresponding runtime component or dependencies.

The palette covers Scheduler, Source, JSONata Mapper, Batch, Gate, Change Filter,
Deduplicate, Data Generator, Rate Limit, Reducer, Retry, Router, HTTP Client,
HTTP Trigger, Logger and ROS 2 Publisher/Subscriber. The installed catalog adds
MQTT, RabbitMQ and Serial components. Each description has its own folder, explicit property
labels, defaults and port metadata.

Each built-in description has a `README.md` in its own folder. These concise component
guides explain purpose, setup, ports and lifecycle behavior. They are embedded in the
Authoring assembly and available as `IComponentDescription.Documentation`, without
Razor or file-system access. Studio displays the Markdown in the Documentation tab.
To add a guide, embed a README with the resource name `<description namespace>.README.md`,
or override the description's `Documentation` property. Descriptions without a guide
keep the Inspector's metadata-based documentation fallback.

Router output ports follow its `Routes` object. Editing routes refreshes the canvas;
renaming or removing a route does not silently rewrite or delete existing links.
Resources remain separate configuration, referenced by name from component fields.
Serial Text needs a workflow-local serial.port resource; ROS 2 needs a runner
registration for a generated message type; HTTP Trigger needs the ASP.NET Core host.
The authoring validator reports these host-dependent checks as not validated.
Reducer and Data Generator expressions use their component-specific runtime context;
their source editors do not claim to evaluate those contexts in Studio.

```csharp
using FluxFlow.Authoring;
using FluxFlow.Authoring.Definitions;
using Microsoft.Extensions.DependencyInjection;

using var services = new ServiceCollection().AddDesignerComponents().BuildServiceProvider();
var descriptions = services.GetServices<IComponentDescription>().ToArray();
var mapper = descriptions.Single(item => item.Palette.ComponentType == "mapper");
// Palette includes ports; Inspector includes property keys, readable labels and editor hints.
var defaults = new Dictionary<string, System.Text.Json.JsonElement>();
mapper.InitializeConfiguration(defaults); // Fresh creation settings, including Engine=jsonata.

var document = new DesignerConfiguration(configurationJson, "Application");
var inspection = document.InspectWorkflow("main", descriptions);
```

`WorkflowNames` lists workflow names; `InspectWorkflow` reads one exact workflow name.
It returns component configuration with configuration-specific presentation/Inspector
metadata, and known links touching the workflow, including incoming and outgoing
cross-workflow declarations. Port discovery uses each owner's configuration-specific
presentation, including owners in other workflows. Duplicate declarations, their direction, Condition,
Engine and filtered Error selection are retained. Unknown components remain visible
with raw configuration and null metadata; warnings identify uninspected link owners,
including owners in other workflows that could contain incoming links. Link names
are never guessed from unknown fields. This is inspection, not runtime validation.

The configuration and link declarations returned by inspection are detached from the
original document. Editing those results does not apply edits. Inspection does not
apply defaults to imported components, construct runtime components, open expression
or Source files, or change configuration/history. Use the explicit editing batch API
to prepare changes. Resources remain preserved in the configuration document; they
are not component entries. SVG icon data is presentation metadata rendered only by
Studio; the bundled icon data retains its notice in `Definitions/Icons.LICENSE.txt`.

## Configuration editing without Studio

`DesignerConfiguration.PrepareEdits` accepts an ordered batch of `AddComponentEdit`,
`ConfigureComponentEdit` and `ConnectPortsEdit`. It returns a detached candidate;
the original document is unchanged, including if a later operation fails. It needs
no UI library, component descriptions, DI provider, runtime instances or file access.

```csharp
using System.Text.Json;
using FluxFlow.Application.Addressing;
using FluxFlow.Authoring;

var document = new DesignerConfiguration("{\"Workflows\":{}}");
var mapper = ComponentAddress.Create("main", "map");
var sink = ComponentAddress.Create("main", "sink");
var candidate = document.PrepareEdits([
    new AddComponentEdit(mapper, "mapper", new Dictionary<string, JsonElement>
    {
        ["Engine"] = JsonSerializer.SerializeToElement("jsonata")
    }),
    new ConfigureComponentEdit(mapper, new Dictionary<string, JsonElement?>
    {
        ["Expression"] = JsonSerializer.SerializeToElement("Reading * 2")
    }),
    new AddComponentEdit(sink, "batch", new Dictionary<string, JsonElement>
    {
        ["BatchSize"] = JsonSerializer.SerializeToElement(2),
        ["MaxWait"] = JsonSerializer.SerializeToElement("00:00:01")
    }),
    new ConnectPortsEdit(mapper.Port("Output"), sink.Port("Input"))
]);
var diagnostics = DesignerConfigurationValidator.Validate(candidate.ToJson());
// The caller decides whether and where to save the accepted configuration.
var configurationJson = candidate.ToApplicationJson();
```

Add creates a missing workflow and rejects duplicate/reserved component names.
Workflow resource sections cannot be targeted as components by Configure or Connect.
Creation settings are explicit; Studio's presets still supply their own defaults.
Configure changes only named top-level properties, retaining existing spelling;
a null dictionary entry removes a property, while a JsonElement containing JSON
null assigns null. Nested JSON values replace the specified property as a whole.
Type is set by Add, not Configure. Connect requires existing component endpoints,
supports cross-workflow links, Condition/Engine, and filtered Output via IsError.
Multiple link declarations remain permitted, matching configuration behavior.

Preparation checks structure and component references. It does not prove that a
runtime type/port exists, compile expressions, read external files or run components.
Use the existing registration validation and runner checks for those responsibilities;
the validator explicitly reports unsupported checks. Existing unknown configuration
is preserved. ToJson retains the outer document/application section; ToApplicationJson
exports only the application. Neither writes to disk. A headless host owns concurrent
file-write checks and persistence; accepting a candidate must not overwrite a file
that changed since the host read it.
