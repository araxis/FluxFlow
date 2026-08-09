using FluxFlow.Composition;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Model;
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
        Definition = builder.Build();
        Input = transform.Input;
        Output = transform.Output;
    }

    internal static ApplicationDefinition Definition { get; }

    internal static InputPortHandle<string> Input { get; }

    internal static OutputPortHandle<string> Output { get; }

    private sealed class UppercaseHandle(ComponentHandle definition)
        : AuthoredComponentHandle(definition)
    {
        internal InputPortHandle<string> Input { get; } = definition.Input<string>("Input");

        internal OutputPortHandle<string> Output { get; } = definition.Output<string>("Output");

        internal OutputPortHandle<ComponentEvent> Events { get; } =
            definition.Output<ComponentEvent>("Events");
    }

    private sealed class UppercaseNode : FlowNode<string, string>
    {
        protected override async Task ProcessAsync(FlowMessage<string> message)
            => await EmitAsync(
                    message.With(message.Value.ToUpperInvariant()),
                    Stopping)
                .ConfigureAwait(false);
    }
}
