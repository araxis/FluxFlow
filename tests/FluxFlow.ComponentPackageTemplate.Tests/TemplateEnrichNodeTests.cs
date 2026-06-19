using FluxFlow.ComponentPackageTemplate;
using FluxFlow.ComponentPackageTemplate.Contracts;
using FluxFlow.ComponentPackageTemplate.Nodes;
using FluxFlow.ComponentPackageTemplate.Options;
using FluxFlow.Nodes;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.ComponentPackageTemplate.Tests;

public sealed class TemplateEnrichNodeTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EnrichesInput_PreservingCorrelation()
    {
        await using var node = new TemplateEnrichNode(
            new TemplateEnrichOptions { Prefix = "demo", BoundedCapacity = 4 },
            new ManualTimeProvider(Now));
        var output = Sink(node.Output);

        var message = FlowMessage.Create(new TemplateInput { Id = "A-100", Value = "order" });
        await node.Input.SendAsync(message);

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.CorrelationId.ShouldBe(message.CorrelationId);
        result.Payload.Id.ShouldBe("A-100");
        result.Payload.Value.ShouldBe("order");
        result.Payload.Text.ShouldBe("demo:order");
        result.Payload.ProcessedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task ReportsInvalidInputOnErrors_AndKeepsProcessing()
    {
        await using var node = new TemplateEnrichNode(
            new TemplateEnrichOptions { Prefix = "demo", BoundedCapacity = 4 });
        var output = Sink(node.Output);
        var errors = Sink(node.Errors);

        var bad = FlowMessage.Create(new TemplateInput { Id = "empty", Value = "" });
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create(new TemplateInput { Id = "valid", Value = "ok" }));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(TemplateErrorCodes.EnrichFailed);
        error.CorrelationId.ShouldBe(bad.CorrelationId);
        error.Context.ShouldBe("id=empty");

        // The next valid input is still processed (the bad one did not fault the pump).
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.Payload.Id.ShouldBe("valid");
        result.Payload.Text.ShouldBe("demo:ok");
    }

    [Fact]
    public async Task EmitsSucceededDiagnostic()
    {
        await using var node = new TemplateEnrichNode(
            new TemplateEnrichOptions { Prefix = "demo", BoundedCapacity = 4 });
        var events = Sink(node.Events);

        var message = FlowMessage.Create(new TemplateInput { Id = "A-100", Value = "order" });
        await node.Input.SendAsync(message);

        var diagnostic = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        diagnostic.Name.ShouldBe(TemplateEnrichNode.Succeeded);
        diagnostic.CorrelationId.ShouldBe(message.CorrelationId);
        diagnostic.Attributes["id"].ShouldBe("A-100");
    }

    [Fact]
    public void RejectsNullOptions()
        => Should.Throw<ArgumentNullException>(() => new TemplateEnrichNode(null!));

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }
}
