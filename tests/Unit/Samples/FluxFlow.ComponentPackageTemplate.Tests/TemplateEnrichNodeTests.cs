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

        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new TemplateInput { Id = "A-100", Value = "order" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        await node.Input.SendAsync(message);

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.CorrelationId.ShouldBe(message.CorrelationId);
        result.TraceId.ShouldBe(message.TraceId);
        result.CausationId.ShouldBe(message.MessageId);
        result.Headers.ShouldBeSameAs(message.Headers);
        result.Value!.Json.Deserialize<TemplateOutput>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Id.ShouldBe("A-100");
        result.Value!.Json.Deserialize<TemplateOutput>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Value.ShouldBe("order");
        result.Value!.Json.Deserialize<TemplateOutput>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Text.ShouldBe("demo:order");
        result.Value!.Json.Deserialize<TemplateOutput>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.ProcessedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task ReportsInvalidInputOnOutput_AndKeepsProcessing()
    {
        await using var node = new TemplateEnrichNode(
            new TemplateEnrichOptions { Prefix = "demo", BoundedCapacity = 4 });
        var output = Sink(node.Output);

        var bad = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new TemplateInput { Id = "empty", Value = "" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        await node.Input.SendAsync(bad);
        await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new TemplateInput { Id = "valid", Value = "ok" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));

        var error = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.IsError.ShouldBeTrue();
        error.Error!.Code.ShouldBe(TemplateErrorCodes.EnrichFailed.ToString());
        error.CorrelationId.ShouldBe(bad.CorrelationId);
        error.Error.Details!.Value.GetProperty("Id").GetString().ShouldBe("empty");

        // The next valid input is still processed (the bad one did not fault the pump).
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.Value!.Json.Deserialize<TemplateOutput>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Id.ShouldBe("valid");
        result.Value!.Json.Deserialize<TemplateOutput>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Text.ShouldBe("demo:ok");
    }

    [Fact]
    public async Task EmitsSucceededDiagnostic()
    {
        await using var node = new TemplateEnrichNode(
            new TemplateEnrichOptions { Prefix = "demo", BoundedCapacity = 4 });
        var events = Sink(node.Events);

        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new TemplateInput { Id = "A-100", Value = "order" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
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
        var malformed = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.Deserialize<JsonElement>(json)));
        var events = Sink(node.Events);
        (await node.Input.SendAsync(malformed)).ShouldBeTrue();
        (await node.Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new TemplateInput { Id = "next", Value = "ok" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();

        var error = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.IsError.ShouldBeTrue();
        error.Error.ShouldNotBeNull().Code.ShouldBe(TemplateErrorCodes.EnrichFailed.ToString());
        error.Error.Category.ShouldBe("template");
        error.Error.Details!.Value.GetProperty("target").GetString().ShouldBe(nameof(TemplateInput));
        error.Error.Details.Value.GetProperty("reason").GetString().ShouldNotBeNullOrWhiteSpace();
        error.CausationId.ShouldBe(malformed.MessageId);
        var diagnostic = (await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).ToFlowEvent();
        diagnostic.Name.ShouldBe(TemplateEnrichNode.Failed);
        diagnostic.Level.ShouldBe(FlowEventLevel.Warning);
        var next = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        next.Value!.Json.Deserialize<TemplateOutput>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Text.ShouldBe("demo:ok");
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
        await application.StartAsync();

        var response = await application.RequestAsync(component.Input, component.Output,
            FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new TemplateInput { Id = "one", Value = "value" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), TimeSpan.FromSeconds(5));

        response.IsError.ShouldBeFalse();
        response.Value!.Json.Deserialize<TemplateOutput>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.ShouldBe(new TemplateOutput
        {
            Id = "one",
            Value = "value",
            Text = "direct:value",
            ProcessedAt = Now
        });
    }

    private static BufferBlock<T> Sink<T>(ISourceBlock<T> source)
    {
        var sink = new BufferBlock<T>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }
}
