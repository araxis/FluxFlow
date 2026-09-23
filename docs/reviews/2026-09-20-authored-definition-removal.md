# Authored definition layer removal — 2026-09-20

The user requested removal of the C# definition-builder, scope and handle layer.
This is an intentional preview API removal, not relocation to a new package.
The JSON document model, configuration loading and native DI composition remain.

## Removed

- `src/FluxFlow.Core/Configuration/ApplicationSetup.cs` (77 lines)
- `src/FluxFlow.Core/Configuration/ApplicationLinks/ApplicationLinkDefinition.cs` (153 lines)
- `src/FluxFlow.Core/Configuration/Definition/ApplicationDefinitionBuilder.cs` (250 lines)
- `src/FluxFlow.Core/Configuration/Definition/ApplicationResourceContract.cs` (90 lines)
- `src/FluxFlow.Core/Configuration/Definition/ApplicationResourceContractCollection.cs` (29 lines)
- `src/FluxFlow.Core/Configuration/Definition/ApplicationResourceDefinitionExtensions.cs` (30 lines)
- `src/FluxFlow.Core/Configuration/Definition/ApplicationSetupExportExtensions.cs` (54 lines)
- `src/FluxFlow.Core/Configuration/Definition/DefinitionHandles.cs` (341 lines)
- `src/FluxFlow.Core/Configuration/Definition/DefinitionPropertyBuilder.cs` (118 lines)
- `src/FluxFlow.Core/Configuration/Definition/DefinitionScope.cs` (17 lines)
- `src/FluxFlow.Core/Configuration/Definition/InputPortConnectionExtensions.cs` (121 lines)
- `src/FluxFlow.Core/Configuration/Definition/ResourceDefinitionContainerBuilder.cs` (171 lines)
- `src/FluxFlow.Core/Configuration/Definition/WorkflowDefinitionBuilder.cs` (382 lines)

Relative to the pre-step source snapshot: 13 production files removed,
1833 lines in those files. Across production C# changes:
9 lines added, 1997 removed, net 1988 fewer lines.
These figures exclude earlier accumulated uncommitted work and test/document changes.

ApplicationSetup embedding/export, authored resource contracts, resource-handle
overloads and the document/native-link merge path are removed with the builders.
FlowJson applies ApplicationDefinition links only. Native C# predicates remain
supported directly through LinkFactory and ports.Link/LinkFallback.
DocumentLinks no longer merges a second authored link collection. The compiler
no longer carries code-condition or declared-handle-type branches.

## Registration path

Configuration registers native factories and selectors in IServiceCollection.
Build resolves them from the same host provider and invokes link delegates.
No new production abstraction, provider, cache or runtime construction path was added.
The plain ComponentDefinition DTO and definition-based registration delegate remain;
they are not builders or handles. Section-based configuration remains unchanged.

## Coverage and validation

Removed authored-API-specific tests, including handle/fluent overload/freeze/export
tests. Migrated mixed suites to real document data and native LinkFactory callbacks.
Preserved condition failures, native predicate exceptions, fallback selection,
delivery policy, backpressure, reference identity, scoped expression selection,
invalid ports, cycles, resource ownership and partial-build cleanup coverage.
Added document-only fallback round-trip and multiple-fallback checks in place of
the corresponding handle tests. The compilation benchmark now constructs plain DTOs.

Release solution build: zero warnings and errors. Full Release run passed 1,994
executions; afterward one redundant boolean theory became a Fact, and both Core
targets passed all 417 cases each. Final suite size is 1,992 executions.
The first exploratory Debug run was stopped after the migrated native test helper
incorrectly attached rejection callbacks to reliable links. The helper was corrected
to match the production/document policy; all migrated tests then passed.

Public API baseline updated and checked. Core and Configuration packages were each
packed into isolated feeds and consumed successfully on .NET 8 and .NET 10.
No publish or commit was performed. Current registration docs updated; older builder
guides explicitly marked historical. Whitespace check passed.

Evidence under `artifacts/cleanup-review-20260920/`:

- `definition-tests-final.log` (full solution)
- `definition-core-final.log` (final Core tests after redundant-case removal)
- `definition-release-final.log`, `definition-api.log`
- `definition-core-package.log`, `definition-configuration-package.log`

## Next decision

This closes the authored-builder removal. Before expanding setup, inspect actual
production consumers of the remaining definition-based registration and link compiler
APIs. Remove another path only if it has no required responsibility; do not replace
the deleted builders with another facade or discard runtime behavior to reduce lines.
