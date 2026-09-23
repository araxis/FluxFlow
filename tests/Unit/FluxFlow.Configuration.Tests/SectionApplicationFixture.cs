using System.Text;
using FluxFlow.Configuration;
using FluxFlow.Core.Application;
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
            registrations.AddComponentFactory(address, _ => create(section));
            ports(registrations, address);
        });

    internal static async Task<(FlowApplication Application, ServiceProvider Provider)> BuildAsync(string json,
        IServiceCollection services, Func<string, IFlowExpressionEngine?>? resolveExpressions = null)
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        if (resolveExpressions is not null)
            services.AddKeyedSingleton<IFlowExpressionEngine>("rules", (_, _) => resolveExpressions("rules")!);
        await services.AddFluxFlowApplicationAsync(configuration);
        var provider = services.BuildServiceProvider();
        try { return (await provider.BuildApplicationAsync(), provider); }
        catch { await provider.DisposeAsync(); throw; }
    }
}
