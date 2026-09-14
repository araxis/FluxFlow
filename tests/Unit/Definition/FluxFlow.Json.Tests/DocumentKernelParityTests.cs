using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Definition;
using FluxFlow.Core;
using FluxFlow.Data;
using FluxFlow.Expressions.CSharp;
using FluxFlow.Mapping;
using FluxFlow.Json.Model;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Json.Tests;

public sealed class DocumentKernelParityTests
{
    [Fact]
    public void Portable_model_is_owned_by_json_without_definition_or_host_dependencies()
    {
        typeof(ApplicationDefinition).Assembly.ShouldBeSameAs(typeof(FlowJson).Assembly);
        typeof(FlowJson).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("FluxFlow.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ShouldBe(["FluxFlow.Core", "FluxFlow.Mapping"]);
    }

    [Fact]
    public async Task Native_predicates_execute_with_portable_conditions_and_preserve_exact_delivery()
    {
        var predicateCalls = 0;
        await VerifyScenarioAsync(async harness =>
        {
            var builder = PortableBuilder(value =>
            {
                Interlocked.Increment(ref predicateCalls);
                return value.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) != 0;
            });
            var setup = builder.Build();
            Should.Throw<NotSupportedException>(() => setup.ToDefinition());
            var application = await setup.BuildApplicationAsync(Bindings(harness), resolveExpressions: harness.Resolve);
            application.GetOutput("main.source.Output").DiscardUnmatched();
            return application;
        }, expectedCompilations: 2);
        predicateCalls.ShouldBe(4);
    }

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    [Fact]
    public async Task Invalid_native_link_disposes_owned_components_without_starting_sources()
    {
        var harness = new Harness();
        var builder = new ApplicationDefinitionBuilder();
        var main = builder.AddWorkflow("main");
        var source = main.AddComponent("source", "sample.source", options => options.Set("Messages", new[] { 1 }));
        var target = main.AddComponent("target", "sample.double");
        source.Output<FlowValue>("Output").LinkTo(target.Input<FlowValue>("Missing"), _ => true);

        var error = await Should.ThrowAsync<KeyNotFoundException>(() => builder.BuildApplicationAsync(Bindings(harness)));

        error.Message.ShouldContain("main.target.Missing");
        harness.SourceRuns.ShouldBe(0);
        harness.Dependency.Constructors.ShouldBe(2);
        harness.Dependency.ComponentDisposals.ShouldBe(2);
        harness.Dependency.Disposed.ShouldBeFalse();
    }

    [Fact]
    public async Task Native_cycle_is_rejected_and_disposes_every_constructed_component()
    {
        var harness = new Harness();
        var builder = new ApplicationDefinitionBuilder();
        var main = builder.AddWorkflow("main");
        var first = main.AddComponent("first", "sample.double");
        var second = main.AddComponent("second", "sample.double");
        first.Output<FlowValue>("Output").LinkTo(second.Input<FlowValue>("Input"), _ => true);
        second.Output<FlowValue>("Output").LinkTo(first.Input<FlowValue>("Input"), _ => true);

        var error = await Should.ThrowAsync<InvalidOperationException>(() => builder.BuildApplicationAsync(Bindings(harness)));

        error.Message.ShouldContain("cycle", Case.Insensitive);
        harness.Dependency.Constructors.ShouldBe(2);
        harness.Dependency.ComponentDisposals.ShouldBe(2);
        harness.Dependency.Disposed.ShouldBeFalse();
    }

    private const string Document = """
        {
          "ExpressionEngine": "defaults",
          "Resources": {},
          "Workflows": {
            "main": {
              "ExpressionEngine": "rules",
              "source": { "Type": "sample.source", "Messages": [2, -1, 4], "Output": "double.Input" },
              "double": {
                "Type": "sample.double",
                "Output": [
                  { "Port": "reports.left.Input", "Condition": "!message.IsError && above(input, minimum)", "Parameters": { "minimum": 0 } },
                  { "Port": "reports.reply.Input", "Condition": "!message.IsError && above(input, minimum)", "Parameters": { "minimum": 0 } },
                  { "Port": "reports.errors.Input", "Condition": "message.IsError" }
                ]
              }
            },
            "reports": {
              "left": { "Type": "sample.left" },
              "reply": { "Type": "sample.reply" },
              "errors": { "Type": "sample.errors" }
            }
          }
        }
        """;

    [Fact]
    public Task Direct_constructors_execute_the_complete_core_scenario()
        => VerifyScenarioAsync(harness =>
        {
            var app = new FlowApplication();
            var main = app.AddWorkflow("main");
            var reports = app.AddWorkflow("reports");
            var source = main.Add("source", new Sequence([2, -1, 4], harness));
            var transform = main.Add("double", new Double(harness.Dependency));
            var left = reports.Add("left", harness.Left = new Sink());
            var reply = reports.Add("reply", harness.Reply = new Reply());
            var errors = reports.Add("errors", harness.Errors = new Sink());
            source.Output.LinkTo(transform.Input);
            left.Input.LinkFrom(transform.Output, message => !message.IsError && message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > 0);
            transform.Output.LinkTo(reply.Input, message => !message.IsError && message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > 0);
            transform.Output.LinkTo(errors.Input, message => message.IsError);
            return Task.FromResult(app);
        });

    [Fact]
    public Task Json_executes_the_same_core_scenario_with_scoped_compiled_conditions()
        => VerifyScenarioAsync(harness => FlowJson.BuildAsync(Document, Bindings(harness),
            resolveExpressions: harness.Resolve), expectedCompilations: 2);

    [Theory]
    [InlineData("input")]
    [InlineData("payload")]
    public Task Json_error_condition_can_reference_a_typed_payload_alias(string alias)
        => VerifyScenarioAsync(harness => FlowJson.BuildAsync(
            Document.Replace("\"Condition\": \"message.IsError\"",
                $"\"Condition\": \"message.IsError || {alias} == null\"", StringComparison.Ordinal),
            Bindings(harness), resolveExpressions: harness.Resolve), expectedCompilations: 2);

    [Fact]
    public Task Portable_fluent_executes_the_same_core_scenario_without_component_creators()
        => VerifyScenarioAsync(harness => PortableBuilder().BuildApplicationAsync(Bindings(harness),
            resolveExpressions: harness.Resolve), expectedCompilations: 2);

    [Fact]
    public Task Portable_links_survive_export_and_execute_on_the_same_kernel()
        => VerifyScenarioAsync(harness =>
        {
            var definition = PortableBuilder().Build();
            definition.Links.Count.ShouldBe(1);
            var json = FlowJson.Serialize(definition.ToDefinition());
            definition.Definition.Workflows["main"].Components["source"].Properties.ContainsKey("Output").ShouldBeFalse();
            using var exported = JsonDocument.Parse(json);
            exported.RootElement.GetProperty("Workflows").GetProperty("main").GetProperty("source")
                .GetProperty("Output").GetString().ShouldBe("double.Input");
            return FlowJson.BuildAsync(json, Bindings(harness), resolveExpressions: harness.Resolve);
        }, expectedCompilations: 2);

    [Fact]
    public void Native_fluent_predicates_cannot_be_silently_exported()
    {
        var builder = new ApplicationDefinitionBuilder();
        var workflow = builder.AddWorkflow("main");
        var source = workflow.AddComponent("source", "sample.source");
        var target = workflow.AddComponent("target", "sample.double");
        source.Output<FlowValue>("Output").LinkTo(target.Input<FlowValue>("Input"), value => value.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > 0);
        var error = Should.Throw<NotSupportedException>(() => builder.Build().ToDefinition());
        error.Message.ShouldContain("Native predicates");
    }

    [Fact]
    public async Task Unknown_document_type_is_rejected_before_any_constructor_runs()
    {
        var harness = new Harness();
        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            FlowJson.BuildAsync(Document.Replace("sample.errors", "sample.unknown", StringComparison.Ordinal), Bindings(harness)));
        error.Message.ShouldContain("sample.unknown");
        harness.Dependency.Constructors.ShouldBe(0);
        harness.SourceRuns.ShouldBe(0);
        harness.Left.ShouldBeNull();
    }

    [Fact]
    public async Task Invalid_document_port_disposes_constructed_components_without_starting_sources()
    {
        var harness = new Harness();
        var error = await Should.ThrowAsync<KeyNotFoundException>(() =>
            FlowJson.BuildAsync(Document.Replace("double.Input", "double.Missing", StringComparison.Ordinal),
                Bindings(harness), resolveExpressions: harness.Resolve));
        error.Message.ShouldContain("double.Missing");
        harness.SourceRuns.ShouldBe(0);
        harness.Dependency.Constructors.ShouldBe(2);
        harness.Dependency.ComponentDisposals.ShouldBe(2);
        harness.Dependency.Disposed.ShouldBeFalse();
        harness.Left!.DisposeCalls.ShouldBe(1);
        harness.Errors!.DisposeCalls.ShouldBe(1);
        harness.Reply!.DisposeCalls.ShouldBe(1);
    }

    private static async Task VerifyScenarioAsync(Func<Harness, Task<FlowApplication>> build, int expectedCompilations = 0)
    {
        var harness = new Harness();
        await using var app = await build(harness);
        app.GetType().ShouldBe(typeof(FlowApplication));
        harness.Dependency.Constructors.ShouldBe(2);
        harness.SourceRuns.ShouldBe(0);
        var replyPort = app.GetOutput("reports.reply.Output");
        try
        {
            await app.StartAsync();
            var response = await app.RequestAsync(app.GetInput("main.double.Input"), replyPort,
                FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(99, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), Deadline);
            response.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(198);
            response.ShouldBeSameAs(harness.Reply!.Messages.Single());
            app.PendingRequestCount.ShouldBe(0);
        }
        finally { harness.Release.TrySetResult(); }
        await app.Completion.WaitAsync(Deadline);
        harness.Left!.Messages.Select(m => m.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([198, 4, 8]);
        harness.Reply!.Messages.Select(m => m.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldBe([198, 4, 8]);
        foreach (var (left, right) in harness.Left.Messages.Zip(harness.Reply.Messages))
        {
            left.ShouldBeSameAs(right);
            left.Value.ShouldBeSameAs(right.Value);
            left.Headers.ShouldBeSameAs(right.Headers);
        }
        harness.Errors!.Messages.Count.ShouldBe(1);
        harness.Errors.Messages.Single().Error!.Code.ShouldBe("node.processing_failed");
        harness.SourceRuns.ShouldBe(1);
        harness.Dependency.Calls.ShouldBe(4);
        harness.Dependency.Disposed.ShouldBeFalse();
        harness.EngineResolutions.ShouldBe(expectedCompilations == 0 ? 0 : 1);
        harness.ExpressionCompilations.ShouldBe(expectedCompilations);
        await app.DisposeAsync();
        harness.Dependency.ComponentDisposals.ShouldBe(2);
        harness.Dependency.Disposed.ShouldBeFalse();
    }

    private static ComponentBindings Bindings(Harness harness)
    {
        var bindings = new ComponentBindings();
        bindings.Add<SourceOptions>("sample.source", options => new Sequence(options.Messages, harness));
        bindings.Add("sample.double", _ => new Double(harness.Dependency));
        bindings.Add("sample.left", _ => harness.Left = new Sink());
        bindings.Add("sample.reply", _ => harness.Reply = new Reply());
        bindings.Add("sample.errors", _ => harness.Errors = new Sink());
        return bindings;
    }

    private static ApplicationDefinitionBuilder PortableBuilder(Func<FlowValue, bool>? nativeCondition = null)
    {
        var builder = new ApplicationDefinitionBuilder().UseExpressions("defaults");
        var main = builder.AddWorkflow("main").UseExpressions("rules");
        var reports = builder.AddWorkflow("reports");
        var source = main.AddComponent("source", "sample.source", options => options.Set("Messages",
            nativeCondition is null ? new[] { 2, -1, 4 } : new[] { 2, 0, -1, 4 }));
        var transform = main.AddComponent("double", "sample.double", options => options.SetJson("Output", OutputDeclarations()));
        reports.AddComponent("left", "sample.left");
        reports.AddComponent("reply", "sample.reply");
        reports.AddComponent("errors", "sample.errors");
        if (nativeCondition is null)
            source.Output<FlowValue>("Output").LinkTo(transform.Input<FlowValue>("Input"));
        else
            source.Output<FlowValue>("Output").LinkTo(transform.Input<FlowValue>("Input"), nativeCondition);
        return builder;
    }

    private static JsonElement OutputDeclarations()
    {
        using var document = JsonDocument.Parse(Document);
        return document.RootElement.GetProperty("Workflows").GetProperty("main").GetProperty("double").GetProperty("Output").Clone();
    }

    private sealed record SourceOptions(int[] Messages);

    private sealed class Harness
    {
        public Dependency Dependency { get; } = new();
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Sink? Left { get; set; }
        public Sink? Errors { get; set; }
        public Reply? Reply { get; set; }
        public int SourceRuns { get; set; }
        public int EngineResolutions { get; private set; }
        public int ExpressionCompilations { get; set; }
        public IFlowExpressionEngine Resolve(string name)
        {
            name.ShouldBe("rules");
            EngineResolutions++;
            return new CountingEngine(new CSharpFlowExpressionEngine(interpreter =>
                interpreter.SetFunction("above", (FlowValue value, long minimum) => value.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) > minimum)), this);
        }
    }

    private sealed class CountingEngine(IFlowExpressionEngine inner, Harness harness) : IFlowExpressionEngine
    {
        public string Name => inner.Name;
        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => throw new InvalidOperationException("Conditions must be compiled before execution.");
        public IFlowCompiledExpression<T> Compile<T>(string expression, IReadOnlyDictionary<string, Type> parameterTypes)
        {
            harness.ExpressionCompilations++;
            return inner.Compile<T>(expression, parameterTypes);
        }
    }

    private sealed class Dependency : IDisposable
    {
        public int Constructors { get; set; }
        public int ComponentDisposals { get; set; }
        public int Calls { get; private set; }
        public bool Disposed { get; private set; }
        public int Double(int value)
        {
            Calls++;
            if (value < 0) throw new InvalidOperationException("Negative input is rejected.");
            return value * 2;
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class Sequence : FlowSource
    {
        private readonly int[] _messages;
        private readonly Harness _harness;
        public Sequence(int[] messages, Harness harness)
        {
            _messages = messages;
            _harness = harness;
            harness.Dependency.Constructors++;
        }
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            _harness.SourceRuns++;
            await _harness.Release.Task.WaitAsync(cancellationToken);
            foreach (var value in _messages) await EmitAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), cancellationToken);
        }
        protected override ValueTask OnDisposeAsync() { _harness.Dependency.ComponentDisposals++; return ValueTask.CompletedTask; }
    }

    private sealed class Double : FlowNode
    {
        private readonly Dependency _dependency;
        public Double(Dependency dependency) { _dependency = dependency; dependency.Constructors++; }
        protected override async Task ProcessAsync(FlowMessage message)
            => await EmitAsync(message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(_dependency.Double(message.Value!.Json.Deserialize<int>()), new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        protected override ValueTask OnDisposeAsync() { _dependency.ComponentDisposals++; return ValueTask.CompletedTask; }
    }

    private sealed class Reply : FlowNode
    {
        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public int DisposeCalls { get; private set; }
        protected override async Task ProcessAsync(FlowMessage message) { Messages.Enqueue(message); await EmitAsync(message); }
        protected override ValueTask OnDisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }

    private sealed class Sink : IFlowComponent
    {
        private readonly ActionBlock<FlowMessage> _input;
        public Sink()
        {
            _input = new(message => Messages.Enqueue(message));
            Ports = new(inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = _input });
        }
        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public int DisposeCalls { get; private set; }
        public FlowPorts Ports { get; }
        public Task Completion => _input.Completion;
        public void Complete() => _input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_input).Fault(error);
        public async ValueTask DisposeAsync() { DisposeCalls++; Complete(); await Completion; }
    }
}
