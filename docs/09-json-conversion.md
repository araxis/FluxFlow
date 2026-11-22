# Configuration providers

Use the standard .NET configuration system to read JSON or another supported source.
FluxFlow.Configuration consumes IConfigurationSection, with no intermediate document
model, custom application serializer or conversion back to JSON.

```csharp
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();
// Register the component/resource adapters used by the configuration first.
services.AddFluxFlowApplication(configuration);
```

The root contains Workflows and optional Resources. A host-selected nested root is
passed with configuration.GetSection("Flows"). Both application and workflow
resources use the same section traversal and per-type ResourceRegistration delegate.
Each registrar binds its own configuration DTO and captures settings for its factory.
Configuration DTOs are separate from runtime options, without shared base classes.

Each component registrar binds its own link properties and explicitly registers
LinkFactory delegates with AddInputLinks/AddOutputLinks/AddErrorLinks. The application
section loop only calls registrars; it does not discover links. Strings, conditional
objects and mixed arrays retain the flat syntax.
See [configuration and links](02-definitions-and-links.md).

IConfiguration represents scalar values as strings, so their target types come from
the specific DTO binding. Source Values uses JsonSource (JSON text or an explicit file) when exact JSON
types must survive provider loading. Link conditions contain their constants directly.
Message/payload JSON serialization is independent and remains.

FluxFlow does not write the application configuration back to a file or preserve
editor metadata. A future editor will define its own persistence requirements.
