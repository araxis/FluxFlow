using FluxFlow.Core.Nodes;

namespace FluxFlow.Application;

/// <summary>Constructs an unstarted component with explicit dependencies.
/// Application coordinates execution; the DI provider owns disposal.</summary>
public delegate IFlowNode ComponentFactory(IServiceProvider services);

/// <summary>
/// Declares links between registered native blocks during application Build.
/// Application Start attaches these links. The provider is the host provider
/// whose workflow scopes own the component instances. Use Link to resolve both endpoints.
/// </summary>
public delegate void LinkFactory(IServiceProvider ports);
