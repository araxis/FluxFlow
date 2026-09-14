using FluxFlow.Testing;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.Definition;
using FluxFlow.Composition.Links;
using FluxFlow.Core;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Json.Links;
using FluxFlow.Json.Model;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Json.Tests;

public sealed partial class PortableDeliveryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Fallback_round_trips_both_directions_and_port_kinds(bool inputSide, bool signal)
    {
        foreach (var delivery in Enum.GetValues<FlowDeliveryPolicy>())
        {
            var builder = new ApplicationDefinitionBuilder();
            var main = builder.AddWorkflow("main");
            var output = main.AddComponent("source", "node").Output<FlowValue>("Output");
            var component = main.AddComponent("target", "node");
            if (signal)
            {
                var target = component.SignalInput("Trigger");
                if (inputSide) target.OtherwiseFrom(output, delivery).ShouldBeSameAs(target);
                else output.OtherwiseTo(target, delivery).ShouldBeSameAs(output);
            }
            else
            {
                var target = component.Input<FlowValue>("Input");
                if (inputSide) target.OtherwiseFrom(output, delivery).ShouldBeSameAs(target);
                else output.OtherwiseTo(target, delivery).ShouldBeSameAs(output);
            }
            var setup = builder.Build();
            var document = ApplicationDefinitionJson.Deserialize(FlowJson.Serialize(setup.ToDefinition()));
            var property = document.Workflows["main"].Components[inputSide ? "target" : "source"]
                .Properties[inputSide ? (signal ? "Trigger" : "Input") : "Output"];
            property.GetProperty("Fallback").GetBoolean().ShouldBeTrue();
            property.TryGetProperty("Condition", out _).ShouldBeFalse();
            property.EnumerateObject().Count().ShouldBe(delivery == FlowDeliveryPolicy.Reliable ? 2 : 3);
            foreach (var result in new[] { new LinkCompilerFixture(Catalog()).Compile(setup),
                         new LinkCompilerFixture(Catalog()).Compile(document) })
            {
                result.Diagnostics.ShouldBeEmpty();
                var link = result.Links.ShouldHaveSingleItem();
                link.IsFallback.ShouldBeTrue();
                link.IsConditional.ShouldBeFalse();
                link.Source.Value.ShouldBe("main.source.Output");
                link.Target.Value.ShouldBe(signal ? "main.target.Trigger" : "main.target.Input");
                link.Delivery.ShouldBe(delivery);
                link.WithDelivery(FlowDeliveryPolicy.RejectWhenFull).IsFallback.ShouldBeTrue();
                var projection = result.Declarations.ShouldHaveSingleItem();
                projection.IsFallback.ShouldBeTrue();
                projection.DeclarationSide.ShouldBe(inputSide ? ApplicationLinkDeclarationSide.Input : ApplicationLinkDeclarationSide.Output);
                ApplicationLinkCompiler.SerializeDeclarations([projection]).EnumerateObject().OrderBy(p => p.Name)
                    .Select(p => (p.Name, p.Value.GetRawText()))
                    .ShouldBe(property.EnumerateObject().OrderBy(p => p.Name).Select(p => (p.Name, p.Value.GetRawText())));
            }
        }
    }

    [Theory]
    [InlineData("\"Fallback\":1")]
    [InlineData("\"Fallback\":\"true\"")]
    [InlineData("\"Fallback\":null")]
    [InlineData("\"fallback\":\"true\"")]
    [InlineData("\"Fallback\":true,\"Condition\":\"true\"")]
    [InlineData("\"Fallback\":true,\"Parameters\":{}")]
    public async Task Invalid_fallback_has_located_diagnostics_and_cleans_owned_components(string property)
    {
        var json = Document("{\"Port\":\"target.Input\"," + property + "}");
        var compiled = new LinkCompilerFixture(Catalog()).Compile(ApplicationDefinitionJson.Deserialize(json));
        compiled.IsValid.ShouldBeFalse();
        compiled.Links.ShouldBeEmpty();
        compiled.Declarations.ShouldBeEmpty();
        compiled.Diagnostics.ShouldContain(d => d.Code == ApplicationLinkDiagnosticCode.InvalidLinkDeclaration &&
            d.Message.Contains("main.source.Output", StringComparison.Ordinal));
        var source = new PassThrough();
        var target = new Sink();
        var error = await Should.ThrowAsync<JsonException>(() => FlowJson.BuildAsync(json, Bindings(source, target)));
        error.Message.ShouldContain("main.source.Output");
        source.Calls.ShouldBe(0);
        source.Disposals.ShouldBe(1);
        target.Disposals.ShouldBe(1);
    }

    [Fact]
    public void Multiple_fallbacks_are_rejected_by_definition_and_document_compilation()
    {
        var builder = new ApplicationDefinitionBuilder();
        var main = builder.AddWorkflow("main");
        var source = main.AddComponent("source", "source").Output<FlowValue>("Output");
        var first = main.AddComponent("target", "target").Input<FlowValue>("Input");
        var second = main.AddComponent("second", "target").Input<FlowValue>("Input");
        source.OtherwiseTo(first);
        Should.Throw<InvalidOperationException>(() => second.OtherwiseFrom(source)).Message.ShouldContain("fallback");
        var setup = builder.Build();
        setup.Links.ShouldHaveSingleItem().Target.ShouldBe(first.Address);
        setup.Links[0].IsFallback.ShouldBeTrue();
        var definitions = setup.ToDefinition();
        var sourceProperties = definitions.Workflows["main"].Components["source"].Properties.ToDictionary(p => p.Key, p => p.Value);
        sourceProperties["Output"] = JsonSerializer.SerializeToElement(new[] {
            new { Port = "target.Input", Fallback = true }, new { Port = "second.Input", Fallback = true } });
        var duplicate = new ApplicationDefinition(workflows: [new("main", new WorkflowDefinition(
            definitions.Workflows["main"].Components.Select(p => new KeyValuePair<string, ComponentDefinition>(p.Key,
                p.Key == "source" ? new ComponentDefinition(p.Value.Type, sourceProperties) : p.Value))))]);
        var compiled = new LinkCompilerFixture(Catalog()).Compile(duplicate);
        compiled.IsValid.ShouldBeFalse();
        compiled.Diagnostics.ShouldContain(d => d.Message.Contains("more than one fallback", StringComparison.Ordinal));
        Should.Throw<ArgumentException>(() => new ApplicationLinkDeclarationProjection(source.Address, first.Address,
            "true", isFallback: true));
    }

    [Fact]
    public void Explicit_false_fallback_remains_an_ordinary_link()
    {
        var definition = ApplicationDefinitionJson.Deserialize(Document("""{"Port":"target.Input","Fallback":false}"""));
        var result = new LinkCompilerFixture(Catalog()).Compile(definition);
        result.Diagnostics.ShouldBeEmpty();
        result.Links.ShouldHaveSingleItem().IsFallback.ShouldBeFalse();
        result.Declarations.ShouldHaveSingleItem().IsFallback.ShouldBeFalse();
        ApplicationLinkCompiler.SerializeDeclarations(result.Declarations).GetString().ShouldBe("target.Input");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Fallback_delivery_is_exclusive_across_document_native_and_contract_graphs(int mode)
    {
        var source = new PassThrough();
        var ordinary = new Sink();
        var fallback = new Sink();
        var engine = new CSharpFlowExpressionEngine(interpreter => interpreter.SetFunction("positive",
            (FlowValue value) => value.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > 0));
        var builder = new ApplicationDefinitionBuilder();
        var main = builder.AddWorkflow("main");
        var output = main.AddComponent("source", "source").Output<FlowValue>("Output");
        var input = main.AddComponent("target", "target").Input<FlowValue>("Input");
        var otherwise = main.AddComponent("healthy", "healthy").Input<FlowValue>("Input");
        output.OtherwiseTo(otherwise);
        if (mode % 2 == 1) input.LinkFrom(output, value => value.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > 0);
        else output.LinkTo(input, "positive(input)");
        var setup = builder.Build();
        KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>[] catalog = [
            new KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>("source", context =>
        {
            var registeredNode = source;
            context.HasInput("Input", source.Input);
            context.HasOutput("Output", source.Output);
            return ValueTask.FromResult<IFlowNode>(registeredNode);
        }),
            new KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>("target", context =>
        {
            var registeredNode = ordinary;
            context.HasInput("Input", ordinary.Input);
            return ValueTask.FromResult<IFlowNode>(registeredNode);
        }),
            new KeyValuePair<string, Func<ComponentActivationContext, ValueTask<IFlowNode>>>("healthy", context =>
        {
            var registeredNode = fallback;
            context.HasInput("Input", fallback.Input);
            return ValueTask.FromResult<IFlowNode>(registeredNode);
        })];
        await using var services = new ServiceCollection().AddTestFactories(catalog).AddSingleton<IFlowExpressionEngine>(engine).BuildServiceProvider();
        var application = mode >= 2 ? await setup.BuildApplicationAsync(services)
            : mode == 1 ? await setup.BuildApplicationAsync(Bindings(source, ordinary, fallback), engine)
            : await FlowJson.BuildAsync(FlowJson.Serialize(setup.ToDefinition()), Bindings(source, ordinary, fallback), engine);
        var messages = new[] { 1, -1, 2, 0 }.Select(value => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToArray();
        await using (application)
        {
            await application.StartAsync();
            foreach (var message in messages)
                (await application.SendAsync(application.GetInput("main.source.Input"), message)).ShouldBeTrue();
            await application.StopAsync().WaitAsync(Deadline);
            ordinary.Messages.ToArray().ShouldBe([messages[0], messages[2]]);
            fallback.Messages.ToArray().ShouldBe([messages[1], messages[3]]);
            ordinary.Messages.First().ShouldBeSameAs(messages[0]);
            fallback.Messages.First().ShouldBeSameAs(messages[1]);
            source.Calls.ShouldBe(4);
            application.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        }
        source.Disposals.ShouldBe(1);
        ordinary.Disposals.ShouldBe(1);
        fallback.Disposals.ShouldBe(1);
    }

    [Theory]
    [InlineData(FlowDeliveryPolicy.Reliable, false)]
    [InlineData(FlowDeliveryPolicy.Reliable, true)]
    [InlineData(FlowDeliveryPolicy.RejectWhenFull, false)]
    [InlineData(FlowDeliveryPolicy.RejectWhenFull, true)]
    [InlineData(FlowDeliveryPolicy.IsolateRejection, false)]
    [InlineData(FlowDeliveryPolicy.IsolateRejection, true)]
    public void Fluent_delivery_round_trips_both_directions_without_expanding_defaults(FlowDeliveryPolicy delivery, bool inputSide)
    {
        var builder = new ApplicationDefinitionBuilder();
        var main = builder.AddWorkflow("main");
        var output = main.AddComponent("source", "node").Output<FlowValue>("Output");
        var input = main.AddComponent("target", "node").Input<FlowValue>("Input");
        if (inputSide) input.LinkFrom(output, delivery).ShouldBeSameAs(input);
        else output.LinkTo(input, delivery).ShouldBeSameAs(output);
        var setup = builder.Build();
        var compiler = new LinkCompilerFixture(Catalog());
        var authored = compiler.Compile(setup);
        authored.Diagnostics.ShouldBeEmpty();
        var json = FlowJson.Serialize(setup.ToDefinition());
        var document = ApplicationDefinitionJson.Deserialize(json);
        var declaration = document.Workflows["main"].Components[inputSide ? "target" : "source"]
            .Properties[inputSide ? "Input" : "Output"];
        var reference = inputSide ? "source.Output" : "target.Input";
        if (delivery == FlowDeliveryPolicy.Reliable) declaration.GetString().ShouldBe(reference);
        else
        {
            declaration.GetProperty("Port").GetString().ShouldBe(reference);
            declaration.GetProperty("Delivery").GetString().ShouldBe(delivery.ToString());
            declaration.EnumerateObject().Count().ShouldBe(2);
        }
        var compiled = compiler.Compile(document);
        compiled.Diagnostics.ShouldBeEmpty();
        foreach (var result in new[] { authored, compiled })
        {
            var link = result.Links.ShouldHaveSingleItem();
            link.Delivery.ShouldBe(delivery);
            link.Source.Value.ShouldBe("main.source.Output");
            link.Target.Value.ShouldBe("main.target.Input");
            link.DeclarationSide.ShouldBe(inputSide ? ApplicationLinkDeclarationSide.Input : ApplicationLinkDeclarationSide.Output);
            link.IsConditional.ShouldBeFalse();
            result.Declarations.ShouldHaveSingleItem().Delivery.ShouldBe(delivery);
            var serialized = ApplicationLinkCompiler.SerializeDeclarations(result.Declarations);
            if (delivery == FlowDeliveryPolicy.Reliable) serialized.GetString().ShouldBe(reference);
            else serialized.EnumerateObject().OrderBy(p => p.Name).Select(p => (p.Name, p.Value.GetString()))
                .ShouldBe(declaration.EnumerateObject().OrderBy(p => p.Name).Select(p => (p.Name, p.Value.GetString())));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Delivery_preserves_scoped_conditions_and_owned_parameters(bool inputSide)
    {
        ApplicationLinkDeclarationProjection projection;
        var parameters = new Dictionary<string, JsonElement>();
        using (var owned = JsonDocument.Parse("7"))
        {
            parameters["minimum"] = owned.RootElement;
            projection = new(ApplicationAddress.WorkflowPort("main", "source", "Output"),
                ApplicationAddress.WorkflowPort("reports", "target", "Input"), "above(input, minimum)",
                inputSide ? ApplicationLinkDeclarationSide.Input : ApplicationLinkDeclarationSide.Output,
                parameters, FlowDeliveryPolicy.IsolateRejection);
        }
        parameters.Clear();
        var declaration = ApplicationLinkCompiler.SerializeDeclarations([projection]).GetRawText();
        var document = ApplicationDefinitionJson.Deserialize(Document(declaration, inputSide, crossWorkflow: true));
        var resolutions = new List<string>();
        var compiler = new LinkCompilerFixture(Catalog(), resolveConditionEngine: name =>
        {
            resolutions.Add(name);
            return new CSharpFlowExpressionEngine(interpreter => interpreter.SetFunction("above",
                (FlowValue input, long minimum) => input.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > minimum));
        });
        var compiled = compiler.Compile(document);
        compiled.Diagnostics.ShouldBeEmpty();
        resolutions.ShouldBe(["rules"]);
        var link = compiled.Links.ShouldHaveSingleItem();
        link.Delivery.ShouldBe(FlowDeliveryPolicy.IsolateRejection);
        link.IsMatch(Context(8)).ShouldBeTrue();
        link.IsMatch(Context(7)).ShouldBeFalse();
        var restored = compiled.Declarations.ShouldHaveSingleItem();
        restored.Parameters["minimum"].GetInt64().ShouldBe(7);
        restored.DeclarationSide.ShouldBe(projection.DeclarationSide);
        ApplicationLinkCompiler.SerializeDeclarations([restored]).GetRawText().ShouldBe(declaration);
    }

    [Theory]
    [InlineData("\"Delivery\":null")]
    [InlineData("\"Delivery\":0")]
    [InlineData("\"Delivery\":\"0\"")]
    [InlineData("\"Delivery\":\"\"")]
    [InlineData("\"Delivery\":\" Reliable\"")]
    [InlineData("\"Delivery\":\"reliabl\"")]
    [InlineData("\"deliveri\":\"Reliable\"")]
    [InlineData("\"Delivery\":\"Unknown\"")]
    [InlineData("\"Delivery\":\"Reliable, RejectWhenFull\"")]
    public async Task Invalid_delivery_has_located_diagnostics_and_cleans_up(string property)
    {
        var json = Document("{\"Port\":\"target.Input\"," + property + "}");
        var compiled = new LinkCompilerFixture(Catalog()).Compile(ApplicationDefinitionJson.Deserialize(json));
        compiled.IsValid.ShouldBeFalse();
        compiled.Links.ShouldBeEmpty();
        compiled.Declarations.ShouldBeEmpty();
        compiled.Diagnostics.ShouldContain(d => d.Code == ApplicationLinkDiagnosticCode.InvalidLinkDeclaration &&
            d.Message.Contains("main.source.Output", StringComparison.Ordinal));
        var source = new PassThrough();
        var sink = new Sink();
        var error = await Should.ThrowAsync<JsonException>(() => FlowJson.BuildAsync(json, Bindings(source, sink)));
        error.Message.ShouldContain("main.source.Output");
        source.Calls.ShouldBe(0);
        source.Disposals.ShouldBe(1);
        sink.Disposals.ShouldBe(1);
    }

    [Fact]
    public async Task Duplicate_delivery_property_is_rejected_before_component_construction()
    {
        var json = Document("{\"Port\":\"target.Input\",\"Delivery\":\"Reliable\",\"Delivery\":\"Reliable\"}");
        var bindings = new ComponentBindings();
        var constructions = 0;
        bindings.Add("node", _ => { constructions++; return new PassThrough(); });
        var error = await Should.ThrowAsync<JsonException>(() => FlowJson.BuildAsync(json, bindings));
        error.Message.ShouldContain("duplicate property 'Delivery'");
        error.Message.ShouldContain("properties.Output");
        constructions.ShouldBe(0);
    }

    [Theory]
    [InlineData(FlowDeliveryPolicy.Reliable, false)]
    [InlineData(FlowDeliveryPolicy.Reliable, true)]
    [InlineData(FlowDeliveryPolicy.RejectWhenFull, false)]
    [InlineData(FlowDeliveryPolicy.RejectWhenFull, true)]
    [InlineData(FlowDeliveryPolicy.IsolateRejection, false)]
    [InlineData(FlowDeliveryPolicy.IsolateRejection, true)]
    [InlineData(FlowDeliveryPolicy.Reliable, false, true)]
    [InlineData(FlowDeliveryPolicy.Reliable, true, true)]
    [InlineData(FlowDeliveryPolicy.RejectWhenFull, false, true)]
    [InlineData(FlowDeliveryPolicy.RejectWhenFull, true, true)]
    [InlineData(FlowDeliveryPolicy.IsolateRejection, false, true)]
    [InlineData(FlowDeliveryPolicy.IsolateRejection, true, true)]
    public async Task Delivery_preserves_condition_failures_and_handler_isolation(FlowDeliveryPolicy delivery, bool native, bool fallback = false)
    {
        var failure = new InvalidOperationException("condition sentinel");
        var source = new PassThrough();
        var target = new Sink();
        var healthy = new Sink();
        var builder = new ApplicationDefinitionBuilder();
        var main = builder.AddWorkflow("main");
        var output = main.AddComponent("source", "source").Output<FlowValue>("Output");
        var input = main.AddComponent("target", "target").Input<FlowValue>("Input");
        if (native) output.LinkTo(input, when: _ => throw failure, delivery);
        else output.LinkTo(input, "fail", delivery);
        var healthyInput = main.AddComponent("healthy", "healthy").Input<FlowValue>("Input");
        if (fallback) output.OtherwiseTo(healthyInput);
        else output.LinkTo(healthyInput);
        var rejected = Channel.CreateUnbounded<(string Source, string Target, FlowMessage Message, Exception Error)>();
        Action<string, string, FlowMessage, Exception> report = (s, t, m, e) =>
        {
            rejected.Writer.TryWrite((s, t, m, e));
            throw new InvalidOperationException("handler sentinel");
        };
        var bindings = Bindings(source, target, healthy);
        var application = native ? await builder.BuildApplicationAsync(bindings, onRejected: report)
            : await FlowJson.BuildAsync(FlowJson.Serialize(builder.Build().ToDefinition()), bindings,
                new ThrowingEngine(failure), onRejected: report);
        try
        {
            await application.StartAsync();
            var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(17, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            (await application.SendAsync(application.GetInput("main.source.Input"), message)).ShouldBeTrue();
            if (delivery == FlowDeliveryPolicy.IsolateRejection)
            {
                var rejection = await rejected.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
                rejection.Source.ShouldBe("main.source.Output");
                rejection.Target.ShouldBe("main.target.Input");
                rejection.Message.ShouldBeSameAs(message);
                rejection.Error.ShouldBeSameAs(failure);
                (await healthy.Received.Reader.ReadAsync().AsTask().WaitAsync(Deadline)).ShouldBeSameAs(message);
                await application.StopAsync().WaitAsync(Deadline);
                application.Completion.IsCompletedSuccessfully.ShouldBeTrue();
                healthy.Messages.ShouldHaveSingleItem().ShouldBeSameAs(message);
            }
            else
            {
                (await Should.ThrowAsync<InvalidOperationException>(() => application.Completion.WaitAsync(Deadline)))
                    .ShouldBeSameAs(failure);
                healthy.Messages.ShouldBeEmpty("Failed condition selection must precede sibling delivery.");
            }
            rejected.Reader.TryRead(out _).ShouldBeFalse();
            target.Messages.ShouldBeEmpty();
            source.Calls.ShouldBe(1);
        }
        finally
        {
            if (delivery == FlowDeliveryPolicy.IsolateRejection) await application.DisposeAsync();
            else
            {
                var disposal = await Should.ThrowAsync<AggregateException>(() => application.DisposeAsync().AsTask());
                disposal.Flatten().InnerExceptions.ShouldAllBe(error => ReferenceEquals(error, failure));
            }
        }
        source.Disposals.ShouldBe(1);
        target.Disposals.ShouldBe(1);
        healthy.Disposals.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conflicting_delivery_is_never_silently_deduplicated(bool mixedSides)
    {
        const string first = "{\"Port\":\"target.Input\",\"Delivery\":\"Reliable\"}";
        const string second = "{\"Port\":\"target.Input\",\"Delivery\":\"RejectWhenFull\"}";
        var json = mixedSides ? Document(first).Replace("\"target\":{\"Type\":\"target\"}",
            "\"target\":{\"Type\":\"target\",\"Input\":{\"Port\":\"source.Output\",\"Delivery\":\"RejectWhenFull\"}}", StringComparison.Ordinal)
            : Document("[" + first + "," + second + "]");
        var compiled = new LinkCompilerFixture(Catalog()).Compile(ApplicationDefinitionJson.Deserialize(json));
        compiled.IsValid.ShouldBeFalse();
        compiled.Diagnostics.ShouldContain(d => d.Code == ApplicationLinkDiagnosticCode.DuplicateLink);
        compiled.Links.Count.ShouldBe(1);
        var source = new PassThrough();
        var sink = new Sink();
        var error = await Should.ThrowAsync<JsonException>(() => FlowJson.BuildAsync(json, Bindings(source, sink)));
        error.Message.ShouldContain("conflicting delivery");
        source.Disposals.ShouldBe(1);
        sink.Disposals.ShouldBe(1);
        source.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Typed_and_signal_definition_retain_policy_with_expressions_and_native_predicates(bool inputSide)
    {
        var builder = new ApplicationDefinitionBuilder();
        var main = builder.AddWorkflow("main");
        var output = main.AddComponent("source", "node").Output<FlowValue>("Output");
        for (var i = 0; i < 6; i++)
        {
            var target = main.AddComponent("target" + i, "node");
            var input = target.Input<FlowValue>("Input");
            var signal = target.SignalInput("Trigger");
            if (inputSide)
            {
                if (i == 0) input.LinkFrom(output, FlowDeliveryPolicy.IsolateRejection);
                if (i == 1) input.LinkFrom(output, "true", FlowDeliveryPolicy.IsolateRejection);
                if (i == 2) input.LinkFrom(output, value => value.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > 7, FlowDeliveryPolicy.IsolateRejection);
                if (i == 3) signal.LinkFrom(output, FlowDeliveryPolicy.IsolateRejection);
                if (i == 4) signal.LinkFrom(output, "true", FlowDeliveryPolicy.IsolateRejection);
                if (i == 5) signal.LinkFrom(output, value => value.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > 7, FlowDeliveryPolicy.IsolateRejection);
            }
            else
            {
                if (i == 0) output.LinkTo(input, FlowDeliveryPolicy.IsolateRejection);
                if (i == 1) output.LinkTo(input, "true", FlowDeliveryPolicy.IsolateRejection);
                if (i == 2) output.LinkTo(input, value => value.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > 7, FlowDeliveryPolicy.IsolateRejection);
                if (i == 3) output.LinkTo(signal, FlowDeliveryPolicy.IsolateRejection);
                if (i == 4) output.LinkTo(signal, "true", FlowDeliveryPolicy.IsolateRejection);
                if (i == 5) output.LinkTo(signal, value => value.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > 7, FlowDeliveryPolicy.IsolateRejection);
            }
        }
        var setup = builder.Build();
        var compiled = new LinkCompilerFixture(Catalog(), new CSharpFlowExpressionEngine()).Compile(setup);
        compiled.Diagnostics.ShouldBeEmpty();
        compiled.Links.Count.ShouldBe(6);
        compiled.Declarations.Count.ShouldBe(4);
        foreach (var link in compiled.Links)
        {
            link.Delivery.ShouldBe(FlowDeliveryPolicy.IsolateRejection);
            link.DeclarationSide.ShouldBe(inputSide ? ApplicationLinkDeclarationSide.Input : ApplicationLinkDeclarationSide.Output);
            link.IsMatch(Context(8)).ShouldBeTrue();
        }
        compiled.Links.Where(link => link.Target.Value.Contains("target2", StringComparison.Ordinal) ||
            link.Target.Value.Contains("target5", StringComparison.Ordinal)).ShouldAllBe(link => !link.IsMatch(Context(7)));
        Should.Throw<NotSupportedException>(() => setup.ToDefinition());
    }

    [Fact]
    public void Invalid_fluent_policy_and_conflicts_leave_existing_declarations_unchanged()
    {
        var builder = new ApplicationDefinitionBuilder();
        var main = builder.AddWorkflow("main");
        var output = main.AddComponent("source", "node").Output<FlowValue>("Output");
        var input = main.AddComponent("target", "node").Input<FlowValue>("Input");
        Should.Throw<ArgumentOutOfRangeException>(() => output.LinkTo(input, (FlowDeliveryPolicy)100));
        main.Connect(output, input, FlowDeliveryPolicy.RejectWhenFull);
        Should.Throw<InvalidOperationException>(() => input.LinkFrom(output, FlowDeliveryPolicy.Reliable));
        var link = builder.Build().Links.ShouldHaveSingleItem();
        link.Delivery.ShouldBe(FlowDeliveryPolicy.RejectWhenFull);
        link.DeclarationSide.ShouldBe(ApplicationLinkDeclarationSide.Output);
        Should.Throw<ArgumentOutOfRangeException>(() => new ApplicationLinkDeclarationProjection(link.Source, link.Target,
            delivery: (FlowDeliveryPolicy)(-1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Application_and_workflow_connect_overloads_forward_delivery(bool applicationLevel)
    {
        var builder = new ApplicationDefinitionBuilder();
        var main = builder.AddWorkflow("main");
        var output = main.AddComponent("source", "node").Output<FlowValue>("Output");
        const FlowDeliveryPolicy policy = FlowDeliveryPolicy.RejectWhenFull;
        Action<ComponentHandle>[] connect = applicationLevel
            ? [c => builder.Connect(output, c.Input<FlowValue>("Input"), policy),
               c => builder.Connect(output, c.Input<FlowValue>("Input"), "true", policy),
               c => builder.Connect(output, c.Input<FlowValue>("Input"), _ => true, policy),
               c => builder.Connect(output, c.SignalInput("Trigger"), policy),
               c => builder.Connect(output, c.SignalInput("Trigger"), "true", policy),
               c => builder.Connect(output, c.SignalInput("Trigger"), _ => true, policy)]
            : [c => main.Connect(output, c.Input<FlowValue>("Input"), policy),
               c => main.Connect(output, c.Input<FlowValue>("Input"), "true", policy),
               c => main.Connect(output, c.Input<FlowValue>("Input"), _ => true, policy),
               c => main.Connect(output, c.SignalInput("Trigger"), policy),
               c => main.Connect(output, c.SignalInput("Trigger"), "true", policy),
               c => main.Connect(output, c.SignalInput("Trigger"), _ => true, policy)];
        for (var i = 0; i < connect.Length; i++) connect[i](main.AddComponent("target" + i, "node"));
        var setup = builder.Build();
        setup.Links.Count.ShouldBe(6);
        setup.Links.ShouldAllBe(link => link.Delivery == policy);
        var result = new LinkCompilerFixture(Catalog(), new CSharpFlowExpressionEngine()).Compile(setup);
        result.Diagnostics.ShouldBeEmpty();
        result.Links.Count.ShouldBe(6);
        result.Links.ShouldAllBe(link => link.Delivery == policy && link.DeclarationSide == ApplicationLinkDeclarationSide.Output);
        result.Links.Count(link => link.IsConditional).ShouldBe(4);
        result.Declarations.Count.ShouldBe(4);
    }

    [Theory]
    [InlineData(FlowDeliveryPolicy.Reliable, false, 2)]
    [InlineData(FlowDeliveryPolicy.Reliable, true, 4)]
    [InlineData(FlowDeliveryPolicy.IsolateRejection, false, 4)]
    [InlineData(FlowDeliveryPolicy.IsolateRejection, true, 2)]
    public async Task Document_and_native_delivery_honor_capacity_and_isolation(FlowDeliveryPolicy delivery, bool native, int capacity)
    {
        var source = new PassThrough();
        var target = new Sink(capacity, hold: true);
        var healthy = new Sink();
        var fillers = Enumerable.Range(0, capacity).Select(i => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(-i - 1, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToArray();
        foreach (var filler in fillers) target.Input.Post(filler).ShouldBeTrue();
        var rejected = Channel.CreateUnbounded<(string Source, string Target, FlowMessage Message, Exception Error)>();
        Action<string, string, FlowMessage, Exception> report = (s, t, m, e) => rejected.Writer.TryWrite((s, t, m, e));
        var builder = Builder(delivery, native);
        var bindings = Bindings(source, target, healthy);
        var application = native ? await builder.BuildApplicationAsync(bindings, onRejected: report)
            : await FlowJson.BuildAsync(FlowJson.Serialize(builder.Build().ToDefinition()), bindings, onRejected: report);
        try
        {
            await application.StartAsync();
            var messages = new[] { FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(11, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(12, new JsonSerializerOptions(JsonSerializerDefaults.Web)))) };
            foreach (var message in messages)
            {
                (await application.SendAsync(application.GetInput("main.source.Input"), message)).ShouldBeTrue();
                (await healthy.Received.Reader.ReadAsync().AsTask().WaitAsync(Deadline)).ShouldBeSameAs(message);
                if (delivery == FlowDeliveryPolicy.IsolateRejection)
                {
                    var failure = await rejected.Reader.ReadAsync().AsTask().WaitAsync(Deadline);
                    failure.Source.ShouldBe("main.source.Output");
                    failure.Target.ShouldBe("main.target.Input");
                    failure.Message.ShouldBeSameAs(message);
                    failure.Error.ShouldBeOfType<InvalidOperationException>();
                    failure.Error.Message.ShouldContain("rejected", Case.Insensitive);
                }
                else target.Release.TrySetResult();
            }
            target.Release.TrySetResult();
            await application.StopAsync().WaitAsync(Deadline);
            await application.Completion.WaitAsync(Deadline);
            target.Messages.ToArray().ShouldBe(delivery == FlowDeliveryPolicy.Reliable ? [.. fillers, .. messages] : fillers);
            healthy.Messages.ToArray().ShouldBe(messages);
            rejected.Reader.TryRead(out _).ShouldBeFalse();
            source.Calls.ShouldBe(2);
        }
        finally
        {
            target.Release.TrySetResult();
            await application.DisposeAsync();
        }
        source.Disposals.ShouldBe(1);
        target.Disposals.ShouldBe(1);
        healthy.Disposals.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Isolated_delivery_requires_handler_before_any_processing(bool native)
    {
        var source = new PassThrough();
        var target = new Sink();
        var healthy = new Sink();
        var builder = Builder(FlowDeliveryPolicy.IsolateRejection, native);
        var bindings = Bindings(source, target, healthy);
        var error = await Should.ThrowAsync<ArgumentNullException>(() => native
            ? builder.BuildApplicationAsync(bindings)
            : FlowJson.BuildAsync(FlowJson.Serialize(builder.Build().ToDefinition()), bindings));
        error.ParamName.ShouldBe("onRejected");
        source.Calls.ShouldBe(0);
        source.Disposals.ShouldBe(1);
        target.Disposals.ShouldBe(1);
        healthy.Disposals.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Immediate_document_delivery_faults_on_full_input_without_waiting(bool native)
    {
        var source = new PassThrough();
        var target = new Sink(capacity: 3, hold: true);
        var healthy = new Sink();
        foreach (var value in new[] { 1, 2, 3 }) target.Input.Post(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ShouldBeTrue();
        var builder = Builder(FlowDeliveryPolicy.RejectWhenFull, native);
        var bindings = Bindings(source, target, healthy);
        var reports = 0;
        Action<string, string, FlowMessage, Exception> report = (_, _, _, _) => Interlocked.Increment(ref reports);
        var application = native ? await builder.BuildApplicationAsync(bindings, onRejected: report)
            : await FlowJson.BuildAsync(FlowJson.Serialize(builder.Build().ToDefinition()), bindings, onRejected: report);
        Exception? failure = null;
        try
        {
            await application.StartAsync();
            var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(99, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            (await application.SendAsync(application.GetInput("main.source.Input"), message)).ShouldBeTrue();
            failure = await Should.ThrowAsync<InvalidOperationException>(() => source.Completion.WaitAsync(Deadline));
            failure.Message.ShouldContain("rejected", Case.Insensitive);
            target.Release.Task.IsCompleted.ShouldBeFalse();
            target.Messages.ShouldNotContain(message);
            reports.ShouldBe(0);
            source.Calls.ShouldBe(1);
            target.Release.TrySetResult();
            (await Should.ThrowAsync<InvalidOperationException>(() => application.Completion.WaitAsync(Deadline)))
                .ShouldBeSameAs(failure);
        }
        finally
        {
            target.Release.TrySetResult();
            var disposal = await Should.ThrowAsync<AggregateException>(() => application.DisposeAsync().AsTask());
            disposal.Flatten().InnerExceptions.ShouldAllBe(error => ReferenceEquals(error, failure));
        }
        source.Disposals.ShouldBe(1);
        target.Disposals.ShouldBe(1);
        healthy.Disposals.ShouldBe(1);
    }

    private static string Document(string declaration, bool inputSide = false, bool crossWorkflow = false)
    {
        var output = inputSide ? "" : ",\"Output\":" + declaration;
        var input = inputSide ? ",\"Input\":" + declaration : "";
        return "{\"Resources\":{},\"Workflows\":{\"main\":{\"ExpressionEngine\":\"rules\",\"source\":{\"Type\":\"source\"" + output + "}," +
            (crossWorkflow ? "\"unused\":{\"Type\":\"node\"}},\"reports\":{\"ExpressionEngine\":\"other\"," : "") +
            "\"target\":{\"Type\":\"target\"" + input + "}}}}";
    }

    private static ApplicationDefinitionBuilder Builder(FlowDeliveryPolicy delivery, bool native)
    {
        var builder = new ApplicationDefinitionBuilder();
        var main = builder.AddWorkflow("main");
        var output = main.AddComponent("source", "source").Output<FlowValue>("Output");
        var target = main.AddComponent("target", "target").Input<FlowValue>("Input");
        if (native) target.LinkFrom(output, value => value.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > 0, delivery);
        else output.LinkTo(target, delivery);
        output.LinkTo(main.AddComponent("healthy", "healthy").Input<FlowValue>("Input"));
        return builder;
    }

    private static ComponentBindings Bindings(PassThrough source, Sink target, Sink? healthy = null)
    {
        var bindings = new ComponentBindings();
        bindings.Add("source", _ => source);
        bindings.Add("target", _ => target);
        if (healthy is not null) bindings.Add("healthy", _ => healthy);
        return bindings;
    }

    private static IReadOnlyDictionary<string, ComponentInstance> Catalog() => LinkCompilerFixture.Surfaces(new[] { "node", "source", "target" }.Select(type => LinkCompilerFixture.Surface(type, inputs: [ComponentPorts.Metadata<FlowValue>("Input"), ComponentPorts.SignalMetadata("Trigger")], outputs: [ComponentPorts.Metadata<FlowValue>("Output")])));

    private static FlowMapContext Context(int value)
    {
        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        return new() { Variables = new Dictionary<string, object?> { ["input"] = message.Value, ["message"] = message } };
    }

    private sealed class PassThrough : FlowNode
    {
        public int Calls { get; private set; }
        public int Disposals { get; private set; }
        protected override async Task ProcessAsync(FlowMessage message) { Calls++; await EmitAsync(message); }
        protected override ValueTask OnDisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class ThrowingEngine(Exception failure) : IFlowExpressionEngine
    {
        public string Name => "test";
        public object? Evaluate(string expression, FlowMapContext context, Type resultType) => throw new NotSupportedException();
        public IFlowCompiledExpression<T> Compile<T>(string expression, IReadOnlyDictionary<string, Type> parameterTypes)
            => (IFlowCompiledExpression<T>)(object)new Compiled(failure);
        private sealed class Compiled(Exception failure) : IFlowCompiledExpression<bool>
        {
            public bool Evaluate(FlowMapContext context) => throw failure;
        }
    }

    private sealed class Sink : IFlowComponent
    {
        public Sink(int capacity = 8, bool hold = false)
        {
            if (!hold) Release.SetResult();
            Input = new ActionBlock<FlowMessage>(async message =>
            {
                Messages.Enqueue(message);
                Received.Writer.TryWrite(message);
                await Release.Task;
            }, new ExecutionDataflowBlockOptions { BoundedCapacity = capacity });
            Ports = new(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = Input });
        }
        public ActionBlock<FlowMessage> Input { get; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Channel<FlowMessage> Received { get; } = Channel.CreateUnbounded<FlowMessage>();
        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public int Disposals { get; private set; }
        public FlowPorts Ports { get; }
        public Task Completion => Input.Completion;
        public void Complete() => Input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)Input).Fault(error);
        public async ValueTask DisposeAsync() { Disposals++; Release.TrySetResult(); Complete(); await Completion; }
    }
}
