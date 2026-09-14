using System.Text.Json;
using FluxFlow.Composition;
using FluxFlow.Composition.Definition;
using FluxFlow.Json.Model;
using FluxFlow.Data;
using FluxFlow.Nodes;

internal static class SampleWorkflow
{
    private static ValueTask<IFlowNode> CreateUppercase(ComponentActivationContext context)
    {
        var createdNode = new UppercaseNode();
        context.HasInput("Input", createdNode.Input);
        context.HasOutput("Output", createdNode.Output);
        context.HasEvents("Events", createdNode.Events);
        return ValueTask.FromResult<IFlowNode>(createdNode);
    }

    private static UppercaseHandle AddUppercase(this WorkflowDefinitionBuilder workflow, string name)
        => workflow.AddComponent(name, "sample.uppercase", CreateUppercase,
            static _ => { }, static component => new UppercaseHandle(component));

    private static WorkflowDefinitionBuilder AddUppercase(this WorkflowDefinitionBuilder workflow, string name, out UppercaseHandle handle)
    {
        handle = workflow.AddUppercase(name);
        return workflow;
    }

    static SampleWorkflow()
    {
        var builder = new ApplicationDefinitionBuilder()
            .AddWorkflow("Operations", out var workflow);

        workflow.AddUppercase("Transform", out var transform);
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
                    message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(message.Value!.Json.Deserialize<string>()!.ToUpperInvariant(), new JsonSerializerOptions(JsonSerializerDefaults.Web)))),
                    Stopping)
                .ConfigureAwait(false);
    }
}
