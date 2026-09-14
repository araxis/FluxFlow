using System.Text.Json;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Composition.Definition;
using FluxFlow.Engine;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Engine.Benchmarks;

internal static class BenchmarkComponents
{
    internal static ValueTask<IFlowNode> CreateEcho(ComponentActivationContext context)
    {
        var node = new EchoNode();
        context.HasInput(node.Input);
        context.HasOutput(node.Output);
        context.HasEvents(node.Events);
        return ValueTask.FromResult<IFlowNode>(node);
    }

}

internal sealed class EchoHandle(ComponentHandle definition) : AuthoredComponentHandle(definition)
{
    internal InputPortHandle<FlowValue> Input { get; } = definition.Input<FlowValue>("Input");

    internal OutputPortHandle<FlowValue> Output { get; } = definition.Output<FlowValue>("Output");
}

internal sealed class EchoNode : FlowNode
{
    internal EchoNode()
        : base(new FlowNodeOptions { InputCapacity = 1_024 })
    {
    }

    protected override async Task ProcessAsync(FlowMessage message)
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
        RequestInput = input.Definition.Input<string>("Input");
        ReplyOutput = output.Definition.Output<string>("Output");
    }

    internal FluxFlowApplication Application { get; }

    internal EchoHandle Input { get; }

    internal EchoHandle Output { get; }

    internal InputPortHandle<string> RequestInput { get; }

    internal OutputPortHandle<string> ReplyOutput { get; }

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
                "benchmark.echo", BenchmarkComponents.CreateEcho, static _ => { }, static component => new EchoHandle(component));
            first ??= current;

            if (previous is not null)
            {
                if (condition is null)
                    previous.Output.LinkTo(current.Input);
                else
                    previous.Output.LinkTo(current.Input, value => condition(value.Json.Deserialize<string>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!));
            }

            previous = current;
        }

        var services = new ServiceCollection();
        services.AddFluxFlowUpdates(
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
