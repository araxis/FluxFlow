using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Composition.Hosting.Revisions;
using FluxFlow.Composition.Revisions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Engine.Hosting;

public static class ApplicationRuntimeAssemblerHostingExtensions
{
    public static ApplicationHostingBuilder UseRuntimeAssembler(
        this ApplicationHostingBuilder hosting,
        Action<ApplicationRuntimeAssemblerBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(hosting);
        if (hosting.Services.Any(static descriptor =>
                descriptor.ServiceType == typeof(IApplicationRevisionCandidateFactory)))
        {
            throw new InvalidOperationException(
                "An application revision candidate factory is already registered.");
        }

        hosting.Services.AddOptions<ApplicationRuntimeAssemblerOptions>();
        hosting.Services.TryAddSingleton(static provider =>
        {
            var registry = new CompositionNodeRegistry();
            foreach (var contributor in provider.GetServices<ICompositionNodeRegistryContributor>())
                contributor.Configure(registry);
            return registry;
        });
        hosting.Services.TryAddSingleton<ApplicationRuntimeAssembler>();
        hosting.Services.TryAddSingleton<IApplicationRuntimeAccess>(static provider =>
            provider.GetRequiredService<ApplicationRuntimeAssembler>());
        hosting.Services.TryAddSingleton<IApplicationRevisionCandidateFactory>(static provider =>
            provider.GetRequiredService<ApplicationRuntimeAssembler>());
        hosting.Services.TryAddSingleton<IApplicationRevisionEventSink>(static provider =>
            provider.GetRequiredService<ApplicationRuntimeAssembler>());

        configure?.Invoke(new ApplicationRuntimeAssemblerBuilder(hosting.Services));
        return hosting;
    }
}
