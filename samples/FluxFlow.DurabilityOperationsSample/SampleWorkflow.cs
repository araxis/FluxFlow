using FluxFlow.Composition;
using FluxFlow.Composition.Authoring;
using FluxFlow.Json.Model;
using FluxFlow.Data;
using FluxFlow.Nodes;

internal static class SampleWorkflow
{
    private static readonly ComponentContract<UppercaseHandle> Uppercase =
        ComponentContract.Create(
            "sample.uppercase",
            static component =>
            {
                component
                    .UseFactory(static _ => new UppercaseNode())
                    .HasInput("Input", static node => node.Input)
                    .HasOutput("Output", static node => node.Output)
                    .HasEvents("Events", static node => node.Events);
            },
            static component => new UppercaseHandle(component));

    static SampleWorkflow()
    {
        var builder = new ApplicationDefinitionBuilder()
            .AddWorkflow("Operations", out var workflow);

        workflow.AddComponent("Transform", Uppercase, out var transform);
        transform.Output.DiscardUnmatched();
        transform.Events.DiscardUnmatched();
        Definition = builder.Build();
        Input = transform.Input;
        Output = transform.Output;
    }

    internal static ApplicationSetup Definition { get; }

    internal static InputPortHandle<FlowValue> Input { get; }

    internal static OutputPortHandle<FlowValue> Output { get; }

    private sealed class UppercaseHandle(ComponentHandle definition)
        : AuthoredComponentHandle(definition)
    {
        internal InputPortHandle<FlowValue> Input { get; } = definition.Input<FlowValue>("Input");

        internal OutputPortHandle<FlowValue> Output { get; } = definition.Output<FlowValue>("Output");

        internal OutputPortHandle<global::FluxFlow.Data.FlowValue> Events { get; } =
            definition.Output<global::FluxFlow.Data.FlowValue>("Events");
    }

    private sealed class UppercaseNode : FlowNode
    {
        protected override async Task ProcessAsync(FlowMessage message)
            => await EmitAsync(
                    message.With(FlowValue.From(message.Value!.Deserialize<string>()!.ToUpperInvariant())),
                    Stopping)
                .ConfigureAwait(false);
    }
}
