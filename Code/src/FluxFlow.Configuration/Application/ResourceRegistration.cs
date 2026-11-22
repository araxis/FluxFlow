using FluxFlow.Application.Addressing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Application;

/// <summary>Reads one resource section and registers its deferred factory in DI.</summary>
public delegate void ResourceRegistration(IServiceCollection services, ApplicationAddress address, IConfigurationSection section);
