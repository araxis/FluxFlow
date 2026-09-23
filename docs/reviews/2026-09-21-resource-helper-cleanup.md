# Resource helper cleanup

After direct IConfiguration registration, three Core helpers still implemented the old
document-to-DI setup path: ComponentDefinition.GetResource, GetRequiredResource, and
IServiceCollection.AddProcessingProfile(ResourceInstanceDefinition). A repository-wide
consumer search found no production callers; the first two had only legacy helper tests,
and profile registration already binds directly in Configuration.

Removed those three methods. Replaced ApplicationResourceRegistration with
FlowResourceExtensions containing the unchanged GetRequiredFlowResource extension.
Resource lookup now exposes one direct path through native keyed DI. No new abstraction,
lookup cache, resource model or component behavior was added. Static-qualified calls
require the new class name; extension syntax and lookup behavior remain unchanged.

Legacy JSON-reference/optional-helper tests were removed with their API. Address tests
now use real ServiceCollection/ServiceProvider rather than a simulated provider and
ComponentDefinition. Coverage verifies exact relative/full addresses, no unrelated-branch
search, workflow/application/host precedence and required-reference errors. Existing
Configuration tests cover profile validation, overrides and deferred resource ownership.
Renamed CompositionNodeFactoryContextTests.cs to match its actual
ComponentConfigurationBindingTests class; remaining explicit document binding tests stay.

Validation: all 1,998 Release executions passed (previously 2,004: 11 removed helper
cases replaced with eight DI cases on each target). Public API baseline updated.
Logs: artifacts/cleanup-review-20260920/resource-helper-tests.log and resource-helper-api.log.
Isolated Core package consumer evidence: resource-helper-consumer.log. No commit/publication.

Remaining document option/processing binding deserves a separate consumer audit.
This step does not delete the independent document serialization/diagnostic API.
