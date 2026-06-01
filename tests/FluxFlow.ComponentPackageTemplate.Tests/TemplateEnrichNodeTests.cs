using FluxFlow.ComponentPackageTemplate.Contracts;
using FluxFlow.ComponentPackageTemplate.Diagnostics;
using FluxFlow.ComponentPackageTemplate.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.ComponentPackageTemplate.Tests;

public sealed class TemplateEnrichNodeTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TemplateEnrichNode_EnrichesInput()
    {
        var runtimeNode = CreateNode(new { prefix = "demo", boundedCapacity = 4 });
        var input = runtimeNode.FindInput(new PortName(TemplateComponentPorts.Input))
            .ShouldBeOfType<InputPort<TemplateInput>>();
        var output = runtimeNode.FindOutput(new PortName(TemplateComponentPorts.Output));
        output.ShouldNotBeNull();
        output.ValueType.ShouldBe(typeof(TemplateOutput));

        var results = new BufferBlock<TemplateOutput>();
        output.TryLinkTo(
            new InputPort<TemplateOutput>(
                new PortAddress("test", new NodeName("results"), new PortName("Input")),
                results),
            propagateCompletion: true,
            out var error);
        error.ShouldBeNull();

        await input.Target.SendAsync(new TemplateInput { Id = "A-100", Value = "order" });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Id.ShouldBe("A-100");
        result.Value.ShouldBe("order");
        result.Text.ShouldBe("demo:order");
        result.ProcessedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task TemplateEnrichNode_ReportsInvalidInputAndContinues()
    {
        var runtimeNode = CreateNode(new { prefix = "demo", boundedCapacity = 4 });
        var input = runtimeNode.FindInput(new PortName(TemplateComponentPorts.Input))
            .ShouldBeOfType<InputPort<TemplateInput>>();
        var errors = new BufferBlock<FlowError>();
        var results = new BufferBlock<TemplateOutput>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(TemplateComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<TemplateOutput>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out _);

        await input.Target.SendAsync(new TemplateInput { Id = "empty", Value = "" });
        await input.Target.SendAsync(new TemplateInput { Id = "valid", Value = "ok" });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(TemplateErrorCodes.EnrichFailed);
        error.Context.ShouldBe("id=empty");

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Id.ShouldBe("valid");
        result.Text.ShouldBe("demo:ok");
    }

    [Fact]
    public async Task TemplateEnrichNode_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(new { prefix = "demo", boundedCapacity = 4 });
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        var input = runtimeNode.FindInput(new PortName(TemplateComponentPorts.Input))
            .ShouldBeOfType<InputPort<TemplateInput>>();
        runtimeNode.FindOutput(new PortName(TemplateComponentPorts.Output))!
            .LinkToDiscard();

        await input.Target.SendAsync(new TemplateInput { Id = "A-100", Value = "order" });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(TemplateDiagnosticNames.EnrichSucceeded);
        diagnostic.Attributes["id"].ShouldBe("A-100");
        diagnostic.Attributes["prefix"].ShouldBe("demo");
    }

    [Fact]
    public void TemplateEnrichNode_RejectsInvalidCapacity()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { prefix = "demo", boundedCapacity = 0 }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void TemplateEnrichNode_RejectsMissingPrefix()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { prefix = "", boundedCapacity = 4 }));

        exception.Message.ShouldContain("prefix");
    }

    private static RuntimeNode CreateNode(object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterTemplateComponents(options =>
                options.UseTimeProvider(new ManualTimeProvider(Now)));

        registry.TryGetFactory(TemplateComponentTypes.Enrich, out var factory).ShouldBeTrue();
        return factory(TemplateTestHost.CreateContext(configuration));
    }
}
