# Local expression engines and resource configuration

## Changes

- Removed ExpressionEngine from application/workflow document constructors and properties,
  JSON scope handling and ordinary section setup. Mapper/Router retain their Engine settings.
- Added Engine beside Condition in the flat link grammar. Both declaration directions,
  cross-workflow addresses, system sources, projections and compilation use the link's key.
  Engine is carried by compiled links and survives WithDelivery/serialization. Runtime link
  identity and compilation cache include the engine key.
- Kept extensible string keys. A framework enum would force changes to Core whenever a host
  adds an engine or independently configured instance. Named keys resolve through native DI.
  Empty/non-string names, names without conditions and fallback/engine combinations reject.
  Explicit unknown keys fail; omitted Engine preserves the host's unkeyed default behavior.
- Deleted ResourceInstanceDefinition, including its generic traversal/reconstruction path.
  ResourceDefinition is now sealed and stores an owned JSON value for explicit document APIs.
  It is not a runtime configuration base class. Each resource registrar binds its own DTO
  using section.Get<TConfiguration>(), validates it and captures settings for its factory.
  Existing processing.profile binding is an example; the direct in-memory regression now
  also binds a resource-specific DTO.
- Renamed DefinitionRules to DocumentValidation. Its shared document validation and copying
  remain; duplicate workflow/segment validation moved to ApplicationAddress's existing contract.
  Removed three unused canonical dictionary lookup helpers. No replacement model or catalog.

## Migration

Old: select ExpressionEngine on application/workflow. New:

```json
{"Port":"other.sink.Input","Condition":"value.amount > 10","Engine":"csharp"}
```

Remove scoped engine fields, including constructor arguments. Legacy fields fail rather than
silently overriding link settings. Resource document callers use ResourceDefinition with raw
JSON/GetResourceValue; runtime registrars use their own typed configuration. There is no
ResourceInstanceDefinition compatibility wrapper.

DocumentValidation remains intentionally: rejecting duplicate names/JSON properties and
owning cloned JSON values are real document responsibilities. This part is a naming and
responsibility correction, not a claim that all validation disappeared.

## Evidence

Artifacts: artifacts/cleanup-review-20260920/local-engine-*.

- Release build: zero warnings/errors.
- Final full suite: 2,014 successful executions, zero failures.
- Existing scoped-selection cases migrated to local link engines; input/output directions,
  cross-workflow delivery, message context, parameters, snapshot and round-trip tests retained.
- New cases cover independent engine compilation within one workflow, retained Engine on
  compiled-link copies, invalid Engine declarations and atomic section rejection.
- Resource document order/ownership and nested lookup tests retained; resource-specific
  IConfiguration binding exercised without a generic resource model.
- Source API baseline reviewed and updated for removed model members and added link Engine.
- Isolated Core and Configuration package consumers validated on .NET 8 and .NET 10.
- No commit, push or publication.
