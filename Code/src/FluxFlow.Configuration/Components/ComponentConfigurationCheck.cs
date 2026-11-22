using Microsoft.Extensions.Configuration;

namespace FluxFlow.Configuration.Components;

/// <summary>Read-only cross-reference validation; never activates resource or component factories.</summary>
public delegate void ComponentConfigurationCheck(IConfiguration application, IConfigurationSection component);
