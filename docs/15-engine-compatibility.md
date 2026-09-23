> Historical API material: live revisions and the update host are removed from the minimum framework. See [current hosting](05-hosting-and-observability.md); old snippets below are not current setup instructions.

# Runtime compatibility

The current checkout consolidates application hosting and revision support in
FluxFlow.Core. Historical Engine package instructions do not describe this source tree.
Mapping contracts are in FluxFlow.Core.Mapping. Component modules are independent
packages and receive explicit dependencies or processing delegates.

Ordinary hosting uses one provider and AddFluxFlow(). AddFluxFlowUpdates is the
optional revision path: candidate providers isolate replacement and rollback.
See [hosting](05-hosting-and-observability.md) for the two entry points.

Consumers migrating from earlier previews must rebuild against coordinated packages.
ComponentRegistration registers synchronous factories and native port selectors;
ComponentActivationContext, ComponentInstance, FlowExecution and metadata records
are removed. Port addresses use ApplicationAddress.Value. ApplicationPorts.Status.Ports
provides current revision port information; there is no Metadata collection.

The existing ApplicationResourceContract revision API remains supported. Current
IConfiguration adapters live separately in FluxFlow.Configuration and do not belong
inside component packages. See [registration](40-component-registration.md).

Published compatibility baselines are separate from the current source-declaration
baseline. These unpublished refactors do not establish binary compatibility with
historical package versions. Use the package compatibility checks before release.
