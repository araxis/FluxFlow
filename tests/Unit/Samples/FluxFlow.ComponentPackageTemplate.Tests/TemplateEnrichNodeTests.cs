using FluxFlow.ComponentPackageTemplate;
using FluxFlow.ComponentPackageTemplate.Contracts;
using FluxFlow.ComponentPackageTemplate.Nodes;
using FluxFlow.ComponentPackageTemplate.Options;
using FluxFlow.Nodes;
using FluxFlow.Core;
using FluxFlow.Data;
using System.Text.Json;
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

        var message = FlowMessage.Create(FlowValue.From(new TemplateInput { Id = "A-100", Value = "order" }));
        await node.Input.SendAsync(message);

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.CorrelationId.ShouldBe(message.CorrelationId);
        result.TraceId.ShouldBe(message.TraceId);
        result.CausationId.ShouldBe(message.MessageId);
        result.Headers.ShouldBeSameAs(message.Headers);
        result.Value!.Deserialize<TemplateOutput>()!.Id.ShouldBe("A-100");
        result.Value!.Deserialize<TemplateOutput>()!.Value.ShouldBe("order");
        result.Value!.Deserialize<TemplateOutput>()!.Text.ShouldBe("demo:order");
        result.Value!.Deserialize<TemplateOutput>()!.ProcessedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task ReportsInvalidInputOnOutput_AndKeepsProcessing()
    {
        await using var node = new TemplateEnrichNode(
            new TemplateEnrichOptions { Prefix = "demo", BoundedCapacity = 4 });
        var output = Sink(node.Output);

        var bad = FlowMessage.Create(FlowValue.From(new TemplateInput { Id = "empty", Value = "" }));
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create(FlowValue.From(new TemplateInput { Id = "valid", Value = "ok" })));

        var error = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.IsError.ShouldBeTrue();
        error.Error!.Code.ShouldBe(TemplateErrorCodes.EnrichFailed.ToString());
        error.CorrelationId.ShouldBe(bad.CorrelationId);
        error.Error.Details!.Value.GetProperty("Id").GetString().ShouldBe("empty");

        // The next valid input is still processed (the bad one did not fault the pump).
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.Value!.Deserialize<TemplateOutput>()!.Id.ShouldBe("valid");
        result.Value!.Deserialize<TemplateOutput>()!.Text.ShouldBe("demo:ok");
    }

    [Fact]
    public async Task EmitsSucceededDiagnostic()
    {
        await using var node = new TemplateEnrichNode(
            new TemplateEnrichOptions { Prefix = "demo", BoundedCapacity = 4 });
        var events = Sink(node.Events);

        var message = FlowMessage.Create(FlowValue.From(new TemplateInput { Id = "A-100", Value = "order" }));
        await node.Input.SendAsync(message);

        var diagnostic = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        diagnostic.ToFlowEvent().Name.ShouldBe(TemplateEnrichNode.Succeeded);
        diagnostic.CorrelationId.ShouldBe(message.CorrelationId);
        diagnostic.ToFlowEvent().Attributes["id"].ShouldBe("A-100");
    }

    [Fact]
    public void RejectsNullOptions()
        => Should.Throw<ArgumentNullException>(() => new TemplateEnrichNode(null!));


    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("42")]
    public async Task Malformed_input_reports_a_domain_error_and_the_next_input_still_runs(string json)
    {
        await using var node = new TemplateEnrichNode(new TemplateEnrichOptions { Prefix = "demo" });
        var output = Sink(node.Output);
        var malformed = FlowMessage.Create(FlowValue.FromJson(JsonSerializer.Deserialize<JsonElement>(json)));
        (await node.Input.SendAsync(malformed)).ShouldBeTrue();
        (await node.Input.SendAsync(FlowMessage.Create(FlowValue.From(new TemplateInput { Id = "next", Value = "ok" })))).ShouldBeTrue();

        var error = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.IsError.ShouldBeTrue();
        error.Error.ShouldNotBeNull().Code.ShouldBe(TemplateErrorCodes.EnrichFailed.ToString());
        error.CausationId.ShouldBe(malformed.MessageId);
        var next = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        next.Value!.Deserialize<TemplateOutput>()!.Text.ShouldBe("demo:ok");
    }

    [Fact]
    public async Task Upstream_error_is_forwarded_as_the_exact_canonical_envelope()
    {
        await using var node = new TemplateEnrichNode(new TemplateEnrichOptions());
        var output = Sink(node.Output);
        var error = FlowMessage.CreateError(new FlowError("upstream", "Already failed.", "test", false));
        (await node.Input.SendAsync(error)).ShouldBeTrue();

        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(error);
    }

    [Fact]
    public async Task Constructed_template_composes_directly_in_the_core_application()
    {
        await using var application = new FlowApplication();
        var node = new TemplateEnrichNode(new TemplateEnrichOptions { Prefix = "direct" }, new ManualTimeProvider(Now));
        var component = application.AddWorkflow("main").Add("enrich", node);
        application.RepliesFrom(component.Output);
        await application.StartAsync();

        var response = await application.RequestAsync(component.Input, component.Output,
            FlowMessage.Create(FlowValue.From(new TemplateInput { Id = "one", Value = "value" })), TimeSpan.FromSeconds(5));

        response.IsError.ShouldBeFalse();
        response.Value!.Deserialize<TemplateOutput>()!.ShouldBe(new TemplateOutput
        {
            Id = "one", Value = "value", Text = "direct:value", ProcessedAt = Now
        });
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }
}
