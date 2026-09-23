using FluxFlow.Core.Nodes;

namespace FluxFlow.Core.Application;

/// <summary>Constructs an unstarted component with explicit dependencies.
/// Application coordinates execution; the DI provider owns disposal.</summary>
public delegate IFlowNode ComponentFactory(IServiceProvider services);

/// <summary>
/// Connects existing instances resolved by address. The provider contains components,
/// native blocks and route-registration delegates. This is the same provider that
/// owns component instances; composition creates no temporary lookup provider.
/// </summary>
public delegate void LinkFactory(IServiceProvider ports);
