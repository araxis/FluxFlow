# Address parts centralized - 2026-09-22

Application composition and Configuration registration repeatedly interpreted address
segments to find workflows, component owners and port names. Moved that knowledge
into the existing ApplicationAddress class as four computed properties:
WorkflowName, ComponentName, PortName and ComponentAddress. No new address model,
endpoint wrapper, cache or service lookup path was added.

ComponentAddress returns the component itself for a component address, its owner
for a workflow port, and null for resources/system ports. WorkflowName also applies
to workflow-scoped resources. Other inapplicable parts return null. Value remains
the unique string used for keyed DI; construction, parsing, casing and equality
rules are unchanged.

Consumers now use these properties for component/port discovery, event stamping,
workflow routing lookup and cycle validation. Link-section registration gets its
workflow from the same contract. Configuration still interprets colon-separated
IConfigurationSection.Path locally, then constructs an ApplicationAddress: this
provider-specific path is not a dotted application address.

This is responsibility/readability cleanup, not a line-count reduction. Four public
properties were added intentionally and reflected in the API baseline. Component
options, port registrations, links, lifecycle and request/reply contracts were not
changed.

Validation:

- All **1,778 Release test executions passed**, including seven new address cases
  on each Core target. Cases cover component/port ownership, app/workflow resources,
  system ports and casing; relative/full address ownership is also checked.
- Public API baseline passed with the four additive properties.
- Isolated Core and Configuration NuGet consumers passed on .NET 8 and .NET 10.
- Application/Configuration no longer slice dotted strings or index Segments to
  obtain these parts. The remaining section.Path.Split belongs to Configuration.

Evidence is under artifacts/cleanup-review-20260920: address-parts-tests.log,
address-parts-api.log, address-parts-core-package.log and
address-parts-configuration-package.log. No commit or publication.
