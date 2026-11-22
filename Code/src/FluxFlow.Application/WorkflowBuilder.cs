using FluxFlow.Application.Addressing;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Application;

/// <summary>Registers a named workflow and its links in the host's service collection.</summary>
public sealed class WorkflowBuilder
{
    public WorkflowBuilder(IServiceCollection services, string name)
    {
        ArgumentNullException.ThrowIfNull(services);
        Services = services.AddFlowWorkflow(name);
        Name = name;
    }

    public IServiceCollection Services { get; }
    public string Name { get; }

    public ComponentAddress Address(string component) => ComponentAddress.Create(Name, component);

    public WorkflowBuilder Link(string output, string input)
    {
        var from = PortAddress.Parse($"{Name}.{output}");
        var to = PortAddress.Parse($"{Name}.{input}");
        Services.AddSingleton<LinkFactory>(provider => provider.Link(from, to));
        return this;
    }
}
