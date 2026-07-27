using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Hosting.DependencyInjection;
using FluxFlow.Composition.Model;
using FluxFlow.Engine;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Hosting.Tests;

#pragma warning disable CS0618

public sealed class CompatibilityHostingTests
{
    [Fact]
    public async Task Legacy_registration_and_host_delegate_to_the_engine_application()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowApplication(new ApplicationDefinition())
            .ConfigureFluxFlowApplication(options =>
            {
                options.InitialRevisionId = "legacy-initial";
                options.StartApplicationWithHost = false;
            });
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        var legacy = provider.GetRequiredService<IApplicationRevisionHost>();

        var started = await legacy.StartApplicationAsync();

        started.Succeeded.ShouldBeTrue();
        started.Update.ShouldBeSameAs(application.LastUpdate);
        legacy.State.ShouldBe(ApplicationRevisionHostState.Running);
        legacy.CurrentDefinition.ShouldBeSameAs(application.CurrentDefinition);

        await legacy.StopApplicationAsync();
        application.State.ShouldBe(ApplicationState.Stopped);
    }

    [Fact]
    public async Task Legacy_definition_source_is_accepted_by_the_engine_registration()
    {
        var source = new StaticApplicationDefinitionSource(new ApplicationDefinition());
        var services = new ServiceCollection();
        services.AddFluxFlowApplication(source);
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<FluxFlowApplication>().StartAsync();

        result.Status.ShouldBe(ApplicationUpdateStatus.Unchanged);
    }

    [Fact]
    public void Legacy_keyed_registration_extensions_forward_to_composition()
    {
        var services = new ServiceCollection();
        var address = ApplicationAddress.WorkflowPort("Orders", "Validate", "Input");
        var target = new BufferBlock<FlowMessage<string>>();

        services.AddExternalFluxFlowInputPort(address, target);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<ITargetBlock<FlowMessage<string>>>(address.Value)
            .ShouldNotBeSameAs(target);
    }
}

#pragma warning restore CS0618
