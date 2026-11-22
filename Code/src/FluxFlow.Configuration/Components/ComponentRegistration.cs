using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components;

/// <summary>Reads a section and registers its factory and port selectors, without creating a component.</summary>
public delegate void ComponentRegistration(IServiceCollection services, IConfigurationSection section);
