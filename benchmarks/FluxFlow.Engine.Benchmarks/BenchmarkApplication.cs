using FluxFlow.Composition;
using FluxFlow.Composition.Authoring;
using FluxFlow.Engine;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Benchmarks;

internal static class BenchmarkComponents
{
    internal static ComponentContract<EchoHandle> Echo { get; } =
        ComponentContract.Create(
            "benchmark.echo",
            static runtime => runtime
                .UseFactory(static _ => new EchoNode())
                .HasInput("Input", static node => node.Input)
                .HasOutput("Output", static node => node.Output)
                .HasEvents("Events", static node => node.Events),
            static component => new EchoHandle(component));
}

internal sealed class EchoHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    internal InputPortHandle<string> Input { get; } = definition.Input<string>("Input");

    internal OutputPortHandle<string> Output { get; } = definition.Output<string>("Output");
}

internal sealed class EchoNode : FlowNode<string, string>
{
    internal EchoNode()
        : base(new FlowNodeOptions { InputCapacity = 1_024 })
    {
    }

    protected override async Task ProcessAsync(FlowMessage<string> message)
        => await EmitAsync(message, Stopping).ConfigureAwait(false);
}

internal sealed class BenchmarkApplication : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private BenchmarkApplication(
        ServiceProvider provider,
        FluxFlowApplication application,
        EchoHandle input,
        EchoHandle output)
    {
        _provider = provider;
        Application = application;
        Input = input;
        Output = output;
    }

    internal FluxFlowApplication Application { get; }

    internal EchoHandle Input { get; }

    internal EchoHandle Output { get; }

    internal static async Task<BenchmarkApplication> StartAsync(
        int hopCount,
        Func<string, bool>? condition = null)
    {
        if (hopCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(hopCount));

        var builder = new ApplicationDefinitionBuilder();
        var workflow = builder.AddWorkflow("main");
        EchoHandle? first = null;
        EchoHandle? previous = null;

        for (var index = 0; index < hopCount; index++)
        {
            var current = workflow.AddComponent(
                $"echo-{index}",
                BenchmarkComponents.Echo);
            first ??= current;

            if (previous is not null)
            {
                if (condition is null)
                    previous.Output.ConnectTo(current.Input);
                else
                    previous.Output.ConnectTo(current.Input, condition);
            }

            previous = current;
        }

        var services = new ServiceCollection();
        services.AddFluxFlow(
            builder.Build(),
            options =>
            {
                options.StartWithHost = false;
                options.InputCapacity = 1_024;
                options.OutputCapacity = 1_024;
            });

        var provider = services.BuildServiceProvider();
        try
        {
            var application = provider.GetRequiredService<FluxFlowApplication>();
            var started = await application.StartAsync().ConfigureAwait(false);
            if (!started.IsApplied)
            {
                throw new InvalidOperationException(
                    $"Benchmark application startup failed: {started.Status}.");
            }

            return new BenchmarkApplication(provider, application, first!, previous!);
        }
        catch
        {
            await provider.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Application.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await _provider.DisposeAsync().ConfigureAwait(false);
        }
    }
}
