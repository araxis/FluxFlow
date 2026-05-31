using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class NodeAuthoringTests
{
    [Fact]
    public async Task BaseNodeHelpers_BuildAndRunPipeline()
    {
        var values = new List<int>();
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new NodeType("test.sequence"), SequenceNode.Create)
            .Register(new NodeType("test.double"), DoubleNode.Create)
            .Register(new NodeType("test.collect"), context => CollectNode.Create(context, values));

        var runtime = BuildRuntime(registry, new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["source"] = new() { Type = new NodeType("test.sequence") },
                        ["double"] = new()
                        {
                            Type = new NodeType("test.double"),
                            Ports =
                            {
                                ["Input"] = JsonValue("source.Output")
                            }
                        },
                        ["collect"] = new()
                        {
                            Type = new NodeType("test.collect"),
                            Ports =
                            {
                                ["Input"] = JsonValue("double.Output")
                            }
                        }
                    }
                }
            }
        });

        await using var _ = runtime;
        await runtime.StartAsync();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        values.ShouldBe([2, 4, 6]);
    }

    [Fact]
    public async Task SinkBase_ReportsProcessingErrorsAndCompletes()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new NodeType("test.sequence"), SequenceNode.Create)
            .Register(new NodeType("test.fail"), FailingSinkNode.Create);

        var runtime = BuildRuntime(registry, new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["main"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["source"] = new() { Type = new NodeType("test.sequence") },
                        ["fail"] = new()
                        {
                            Type = new NodeType("test.fail"),
                            Ports =
                            {
                                ["Input"] = JsonValue("source.Output")
                            }
                        }
                    }
                }
            }
        });

        var sink = runtime.Workflows.Single().Nodes.Single(node => node.Address.Node.Value == "fail");
        var errors = new BufferBlock<FlowError>();
        sink.Node.Errors.LinkTo(errors);

        await using var _ = runtime;
        await runtime.StartAsync();
        await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        errors.TryReceive(out var error).ShouldBeTrue();
        error.Code.ShouldBe(FlowErrorCodes.ProcessingFailed);
        error.Message.ShouldContain("failed to process input");
    }

    [Fact]
    public void RegistrationContract_AddsFactoryToRegistry()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .Register(new SequenceNodeRegistration());

        registry.TryGetFactory(new NodeType("test.sequence"), out var factory).ShouldBeTrue();
        factory.ShouldNotBeNull();
    }

    private static ApplicationRuntime BuildRuntime(
        RuntimeNodeFactoryRegistry registry,
        ApplicationDefinition definition)
    {
        var result = new ApplicationRuntimeBuilder(registry).Build(definition);
        result.IsSuccess.ShouldBeTrue(string.Join(Environment.NewLine, result.Errors.Select(e => e.Message)));
        return result.Runtime!;
    }

    private static System.Text.Json.JsonElement JsonValue(string value)
        => System.Text.Json.JsonSerializer.SerializeToElement(value);

    private sealed class SequenceNode : SourceFlowNode<int>
    {
        public static RuntimeNode Create(RuntimeNodeFactoryContext context)
        {
            var node = new SequenceNode();
            return context.CreateNode(node)
                .Output("Output", node.OutputBlock)
                .Build();
        }

        public override async Task StartAsync(CancellationToken cancellationToken = default)
        {
            for (var value = 1; value <= 3; value++)
            {
                await SendOutputAsync(value, cancellationToken);
            }

            CompleteOutput();
        }
    }

    private sealed class DoubleNode : MapFlowNode<int, int>
    {
        public static RuntimeNode Create(RuntimeNodeFactoryContext context)
        {
            var node = new DoubleNode();
            return context.CreateNode(node)
                .Input("Input", node.InputBlock)
                .Output("Output", node.OutputBlock)
                .Build();
        }

        protected override ValueTask<int> MapAsync(
            int input,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(input * 2);
    }

    private sealed class CollectNode(List<int> values) : SinkFlowNode<int>
    {
        public static RuntimeNode Create(
            RuntimeNodeFactoryContext context,
            List<int> values)
        {
            var node = new CollectNode(values);
            return context.CreateNode(node)
                .Input("Input", node.InputBlock)
                .Build();
        }

        protected override ValueTask HandleAsync(
            int input,
            CancellationToken cancellationToken)
        {
            values.Add(input);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FailingSinkNode : SinkFlowNode<int>
    {
        public static RuntimeNode Create(RuntimeNodeFactoryContext context)
        {
            var node = new FailingSinkNode();
            return context.CreateNode(node)
                .Input("Input", node.InputBlock)
                .Build();
        }

        protected override ValueTask HandleAsync(
            int input,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("Nope.");
    }

    private sealed class SequenceNodeRegistration : IFlowNodeRegistration
    {
        public NodeType Type { get; } = new("test.sequence");

        public RuntimeNode Create(RuntimeNodeFactoryContext context)
            => SequenceNode.Create(context);
    }
}
