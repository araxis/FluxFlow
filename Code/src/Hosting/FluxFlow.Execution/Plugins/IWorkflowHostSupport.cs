using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Execution.Plugins;

/// <summary>Shares host-owned services with a run and checks their availability without starting a workflow.</summary>
public interface IWorkflowHostSupport
{
    void ConfigureInstance(IServiceCollection instance);
    Task CheckAsync(IConfiguration configuration, CancellationToken cancellationToken);
}
