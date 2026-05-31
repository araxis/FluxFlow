using FluxFlow.Engine.Components;
using FluxFlow.Engine.Core;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Engine.Tests;

/// <summary>
/// Smoke tests verifying the engine's runtime builder compiles graphs correctly.
/// These tests use a protocol-neutral NoopNode.
/// </summary>
public sealed class RuntimeBuilderTests
{
    [Fact]
    public void Build_EmptyWorkflow_ReturnsValidationError()
    {
        // The engine requires at least one node per workflow — empty workflows are rejected.
        var builder = new ApplicationRuntimeBuilder(new RuntimeNodeFactoryRegistry());

        var definition = new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition> { ["smoke"] = new() }
        };

        var result = builder.Build(definition);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Build_DefinitionWithNoWorkflows_ReturnsValidationError()
    {
        // A definition with zero workflows is rejected.
        var builder = new ApplicationRuntimeBuilder(new RuntimeNodeFactoryRegistry());
        var result = builder.Build(new ApplicationDefinition());

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.Message.Contains("workflow"));
    }

    [Fact]
    public void Build_UnknownNodeType_ReturnsError()
    {
        var builder = new ApplicationRuntimeBuilder(new RuntimeNodeFactoryRegistry());

        var definition = new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["smoke"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["n1"] = new NodeDefinition { Type = new NodeType("test.unknown") }
                    }
                }
            }
        };

        var result = builder.Build(definition);

        result.IsSuccess.ShouldBeFalse();
        result.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Build_RegisteredNodeType_Succeeds_AndNodeStarts()
    {
        var registry = new RuntimeNodeFactoryRegistry();
        registry.Register(
            new NodeType("test.noop"),
            ctx => RuntimeNode.Create(ctx.Address, new NoopNode(), phase: ctx.Definition.Phase));

        var builder = new ApplicationRuntimeBuilder(registry);

        var definition = new ApplicationDefinition
        {
            Workflows = new Dictionary<string, WorkflowDefinition>
            {
                ["smoke"] = new()
                {
                    Nodes = new Dictionary<string, NodeDefinition>
                    {
                        ["n1"] = new NodeDefinition { Type = new NodeType("test.noop") }
                    }
                }
            }
        };

        var result = builder.Build(definition);

        result.IsSuccess.ShouldBeTrue();

        await using var runtime = result.Runtime!;
        await runtime.StartAsync();           // should not throw
        runtime.Complete();
        await runtime.Completion;             // drains cleanly
    }

    [Fact]
    public void DefinitionJson_IgnoresWorkspaceMetadata()
    {
        var json = """
        {
          "workflows": {
            "smoke": {
              "n1": { "type": "test.noop" }
            }
          },
          "dashboards": {
            "main": {
              "widgets": {
                "unused": { "type": "demo.widget" }
              }
            }
          },
          "tests": {
            "smoke": {
              "steps": {
                "unused": { "type": "expect.event" }
              }
            }
          }
        }
        """;

        var definition = JsonSerializer.Deserialize<ApplicationDefinition>(
            json,
            ApplicationDefinitionJson.CreateSerializerOptions());

        definition.ShouldNotBeNull();
        definition.Workflows.Keys.ShouldBe(["smoke"]);
    }

    // ── Minimal protocol-neutral IFlowNode ────────────────────────────────────
    private sealed class NoopNode : IFlowNode
    {
        private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FlowNodeId Id { get; } = FlowNodeId.New();
        public ISourceBlock<FlowError> Errors { get; } = new BufferBlock<FlowError>();
        public Task Completion => _tcs.Task;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Complete() => _tcs.TrySetResult();
        public void Fault(Exception exception) => _tcs.TrySetException(exception);
    }
}
