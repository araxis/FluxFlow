using FluxFlow.Composition;
using FluxFlow.Composition.Model;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

internal static class SampleWorkflow
{
    internal static ApplicationDefinition Definition { get; } = new(
        workflows:
        [
            new("Operations", new WorkflowDefinition(
            [
                new("Transform", new ComponentDefinition("sample.uppercase"))
            ]))
        ]);

    internal static void RegisterComponents(IServiceCollection services)
        => services.AddFluxFlowComponents()
            .AddRuntimeComponent("sample.uppercase", component =>
            {
                component.UseFactory(_ =>
                {
                    var node = new UppercaseNode();
                    return ValueTask.FromResult(ComponentInstance.Create(
                        node,
                        inputs: [ComponentPorts.Input<string>("Input", node.Input)],
                        outputs: [ComponentPorts.Output<string>("Output", node.Output)]));
                });
                component.AddInput<string>("Input");
                component.AddOutput<string>("Output");
            });

    private sealed class UppercaseNode : FlowNode<string, string>
    {
        protected override async Task ProcessAsync(FlowMessage<string> message)
            => await EmitAsync(
                    message.With(message.Value.ToUpperInvariant()),
                    Stopping)
                .ConfigureAwait(false);
    }
}
