# Configuration and native registration

The authored C# definition-builder and handle API has been removed from the minimum
framework. Configuration reads flat component sections and registers factories and
native port selectors in the host's IServiceCollection. Build resolves those services
from the same provider and applies the registered LinkFactory callbacks.

Use AddComponentFactory, AddInputPort, AddOutputPort and AddEventPort for a component.
Use ports.Link and ports.LinkFallback inside LinkFactory for native C# predicates.
Configuration registers the same link delegates while reading each component section.
There is no separate serializable definition model or document application path.

Resources follow the same registration/build boundary through ResourceRegistration.
Neither component implementations nor their options know about configuration loading.

See [component registration](40-component-registration.md) and
[Configuration package](../src/FluxFlow.Configuration/README.md) for current examples.
