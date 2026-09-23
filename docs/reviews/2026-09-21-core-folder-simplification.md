# Core folder simplification

This step changes physical organization and matching namespaces only. No feature,
component contract member or execution behavior was removed.

| Previous location | Current location |
| --- | --- |
| Configuration/Configuration/CompositionConfigurationException.cs | Configuration/CompositionConfigurationException.cs |
| Configuration/Runtime/ComponentActivityBridge.cs | Application/ComponentActivityBridge.cs |
| Ports/FlowLink.cs | Execution/FlowLink.cs |
| Configuration/Json/Model (seven model files) | Configuration/Model |
| Configuration/Json/Model/ApplicationDefinitionJson.cs | Configuration/Json/ApplicationDefinitionJson.cs |
| Configuration/Json/Model/ApplicationDefinitionJsonConverter.cs | Configuration/Json/ApplicationDefinitionJsonConverter.cs |

Twelve files moved. Eight empty directories removed: Data, Ports,
Configuration/Definition, Configuration/Factories, Configuration/Registration,
Configuration/Configuration, Configuration/Runtime and Configuration/Json/Model.
Configuration/Model is the only newly introduced directory.

Core now has nine source folders at its root: Addressing, Application,
Configuration, Coordination, Execution, Mapping, Messaging, Nodes and Resilience.
Existing meaningful responsibility boundaries were retained.

Namespaces match the new locations. In particular, consumers now use:

- FluxFlow.Core.Configuration.Model for document model classes.
- FluxFlow.Core.Configuration.Json for ApplicationDefinitionJson.
- FluxFlow.Core.Execution for FlowLink and FlowDeliveryPolicy.
- FluxFlow.Core.Configuration for CompositionConfigurationException.

This is a source/binary compatibility change to the development-preview API:
consumers need updated imports and a rebuild. There are no forwarding wrappers.
Source, tests, benchmarks, isolated consumers and current documentation were updated.
Historical reports retain their original paths. The SDK project includes source
files automatically, so Visual Studio reflects the physical folder layout without
an additional solution-folder layer or explicit project-file entries.

Validation evidence is in artifacts/cleanup-review-20260920:

- folders-build.log: Release build, zero warnings/errors.
- folders-api.log: intentional public namespace baseline updated and checked.
- folders-tests.log: full Release suite, 1,986 passed.
- folders-core-package.log and folders-configuration-package.log: isolated package
  consumers on .NET 8 and .NET 10.

Whitespace check passed. No commit or publication.
