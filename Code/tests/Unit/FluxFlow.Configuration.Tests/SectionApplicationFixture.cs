using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components;
using System.Text;
using FluxFlow.Configuration.Application.Links;
using FluxFlow.Application;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Test-only registration convenience; uses the production section traversal and provider.
internal static class SectionApplicationFixture
{
    internal static void AddTestComponent<T>(this IServiceCollection services, string type,
        Func<IConfigurationSection, T> create, Action<IServiceCollection, string> ports) where T : IFlowNode
        => services.AddKeyedSingleton<ComponentRegistration>(type, (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            registrations.AddComponentFactory(ComponentAddress.Parse(address.Value), _ => create(section));
            ports(registrations, address.Value);
            var config = section.ReadConfiguration<TestPortConfiguration>()!;
            registrations.AddInputLinks(address.Port("Input"), config.Input)
                .AddInputLinks(address.Port("Reply"), config.Reply)
                .AddOutputLinks(address.Port("Output"), config.Output)
                .AddOutputLinks(address.Port("Events"), config.Events)
                .AddErrorLinks(address.Port("Output"), config.Error);
        });

    internal static async Task<(FlowApplication Application, ServiceProvider Provider)> BuildAsync(string json,
        IServiceCollection services, Func<string, IFlowExpressionEngine?>? resolveExpressions = null)
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        if (resolveExpressions is not null)
            services.AddKeyedSingleton<IFlowExpressionEngine>("rules", (_, _) => resolveExpressions("rules")!);
        services.AddFluxFlowApplication(configuration);
        var provider = services.BuildServiceProvider();
        try { return (await provider.BuildApplicationAsync(), provider); }
        catch { await provider.DisposeAsync(); throw; }
    }
}

// Data-only DTO for test component registrars; no port discovery or DI lookup.
internal sealed class TestPortConfiguration
{
    public OneOrManyPortReferences Input { get; init; } = OneOrManyPortReferences.Empty;
    public OneOrManyPortReferences Output { get; init; } = OneOrManyPortReferences.Empty;
    public OneOrManyPortReferences Events { get; init; } = OneOrManyPortReferences.Empty;
    public OneOrManyPortReferences Reply { get; init; } = OneOrManyPortReferences.Empty;
    public OneOrManyPortReferences Error { get; init; } = OneOrManyPortReferences.Empty;
}
