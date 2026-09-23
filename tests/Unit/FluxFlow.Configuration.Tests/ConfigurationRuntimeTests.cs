using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Application;
using FluxFlow.Core.Execution;
using FluxFlow.Core.Messaging;
using FluxFlow.Testing;
using FluxFlow.Core.Mapping;
using FluxFlow.Core.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;

public sealed class ConfigurationRuntimeTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);
    [Fact]
    public async Task Invalid_native_link_disposes_owned_components_without_starting_sources()
    {
        var harness = new Harness();
        var bindings = Bindings(harness);
        bindings.AddSingleton<LinkFactory>(ports => ports.Link("main.source.Output", "main.target.Missing", _ => true));
        var error = await Should.ThrowAsync<KeyNotFoundException>(() => SectionApplicationFixture.BuildAsync(
            """{"Workflows":{"main":{"source":{"Type":"sample.source","Messages":[1]},"target":{"Type":"sample.double"}}}}""", bindings));

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
        var bindings = Bindings(harness);
        bindings.AddSingleton<LinkFactory>(ports =>
        {
            ports.Link("main.first.Output", "main.second.Input", _ => true);
            ports.Link("main.second.Output", "main.first.Input", _ => true);
        });
        var error = await Should.ThrowAsync<InvalidOperationException>(() => SectionApplicationFixture.BuildAsync(
            """{"Workflows":{"main":{"first":{"Type":"sample.double"},"second":{"Type":"sample.double"}}}}""", bindings));

        error.Message.ShouldContain("cycle", Case.Insensitive);
        harness.Dependency.Constructors.ShouldBe(2);
        harness.Dependency.ComponentDisposals.ShouldBe(2);
        harness.Dependency.Disposed.ShouldBeFalse();
    }

    private const string Document = """
        {
          "Resources": {},
          "Workflows": {
            "main": {
              "source": { "Type": "sample.source", "Messages": [2, -1, 4], "Output": "double.Input" },
              "double": {
                "Type": "sample.double",
                "Output": [
                  { "Port": "reports.left.Input", "Engine": "rules", "Condition": "!message.IsError && above(input, minimum)", "Parameters": { "minimum": 0 } },
                  { "Port": "reports.reply.Input", "Engine": "rules", "Condition": "!message.IsError && above(input, minimum)", "Parameters": { "minimum": 0 } },
                  { "Port": "reports.errors.Input", "Engine": "rules", "Condition": "message.IsError" }
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
    public Task Native_DI_registrations_execute_the_complete_core_scenario()
        => VerifyScenarioAsync(async harness =>
        {
            var services = new ServiceCollection()
                .AddComponentFactory("main.source", _ => new Sequence([2, -1, 4], harness))
                .AddOutputPort<Sequence>("main.source", "Output", node => node.Output)
                .AddComponentFactory("main.double", _ => new Double(harness.Dependency))
                .AddInputPort<Double>("main.double", "Input", node => node.Input)
                .AddOutputPort<Double>("main.double", "Output", node => node.Output)
                .AddComponentFactory("reports.left", _ => harness.Left = new Sink())
                .AddInputPort<Sink>("reports.left", "Input", node => node.Input)
                .AddComponentFactory("reports.reply", _ => harness.Reply = new Reply())
                .AddInputPort<Reply>("reports.reply", "Input", node => node.Input)
                .AddOutputPort<Reply>("reports.reply", "Output", node => node.Output)
                .AddComponentFactory("reports.errors", _ => harness.Errors = new Sink())
                .AddInputPort<Sink>("reports.errors", "Input", node => node.Input);
            services.AddSingleton<LinkFactory>(ports =>
            {
                ports.Link("main.source.Output", "main.double.Input");
                ports.Link("main.double.Output", "reports.left.Input", message => !message.IsError && message.Value.Json.GetInt32() > 0);
                ports.Link("main.double.Output", "reports.reply.Input", message => !message.IsError && message.Value.Json.GetInt32() > 0);
                ports.Link("main.double.Output", "reports.errors.Input", message => message.IsError);
            });
            harness.Captured = OutputProbe.Register(services, "reports.reply.Output").Messages;
            var provider = services.BuildServiceProvider();
            try { return (await provider.BuildApplicationAsync(), provider); }
            catch { await provider.DisposeAsync(); throw; }
        });

    [Fact]
    public Task Json_executes_the_same_core_scenario_with_scoped_compiled_conditions()
        => VerifyScenarioAsync(harness => SectionApplicationFixture.BuildAsync(Document, Bindings(harness),
            resolveExpressions: harness.Resolve), expectedCompilations: 2);

    [Theory]
    [InlineData("input")]
    [InlineData("payload")]
    public Task Json_error_condition_can_reference_a_typed_payload_alias(string alias)
        => VerifyScenarioAsync(harness => SectionApplicationFixture.BuildAsync(
            Document.Replace("\"Condition\": \"message.IsError\"",
                $"\"Condition\": \"message.IsError || {alias} == null\"", StringComparison.Ordinal),
            Bindings(harness), resolveExpressions: harness.Resolve), expectedCompilations: 2);

    [Fact]
    public async Task Unknown_document_type_is_rejected_before_any_constructor_runs()
    {
        var harness = new Harness();
        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            SectionApplicationFixture.BuildAsync(Document.Replace("sample.errors", "sample.unknown", StringComparison.Ordinal), Bindings(harness)));
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
            SectionApplicationFixture.BuildAsync(Document.Replace("double.Input", "double.Missing", StringComparison.Ordinal),
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

    private static async Task VerifyScenarioAsync(Func<Harness, Task<(FlowApplication Application, ServiceProvider Provider)>> build, int expectedCompilations = 0)
    {
        var harness = new Harness();
        var (app, provider) = await build(harness);
        await using var providerLifetime = provider;
        await using var appExecution = app;
        app.GetType().ShouldBe(typeof(FlowApplication));
        harness.Dependency.Constructors.ShouldBe(2);
        harness.SourceRuns.ShouldBe(0);
        try
        {
            await appExecution.StartAsync();
            (await ((Double)appExecution.GetComponent("main.double")).Input.SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(99, new JsonSerializerOptions(JsonSerializerDefaults.Web)))))).ShouldBeTrue();
            var response = await harness.Captured!.ReceiveAsync(Deadline);
            response.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(198);
            response.ShouldBeSameAs(harness.Reply!.Messages.Single());

        }
        finally { harness.Release.TrySetResult(); }
        await appExecution.Completion.WaitAsync(Deadline);
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
        await appExecution.DisposeAsync();
        harness.Dependency.ComponentDisposals.ShouldBe(0);
        await provider.DisposeAsync();
        harness.Dependency.ComponentDisposals.ShouldBe(2);
        harness.Dependency.Disposed.ShouldBeFalse();
    }

    private static ServiceCollection Bindings(Harness harness)
    {
        var bindings = new ServiceCollection();
        bindings.AddTestComponent("sample.source", definition => new Sequence(definition.GetSection("Messages").Get<int[]>()!, harness),
            (services, address) => services.AddOutputPort<Sequence>(address, "Output", node => node.Output).AddEventPort<Sequence>(address, node => node.Events));
        bindings.AddTestComponent("sample.double", _ => new Double(harness.Dependency), (services, address) => services.AddInputPort<Double>(address, "Input", node => node.Input)
            .AddOutputPort<Double>(address, "Output", node => node.Output)
            .AddEventPort<Double>(address, node => node.Events));
        bindings.AddTestComponent("sample.left", _ => harness.Left = new Sink(), (services, address) => services.AddInputPort<Sink>(address, "Input", node => node.Input));
        bindings.AddTestComponent("sample.reply", _ => harness.Reply = new Reply(), (services, address) =>
        {
            services.AddInputPort<Reply>(address, "Input", node => node.Input)
                .AddOutputPort<Reply>(address, "Output", node => node.Output)
                .AddEventPort<Reply>(address, node => node.Events);
            harness.Captured = OutputProbe.Register(services, address + ".Output").Messages;
        });
        bindings.AddTestComponent("sample.errors", _ => harness.Errors = new Sink(), (services, address) => services.AddInputPort<Sink>(address, "Input", node => node.Input));
        return bindings;
    }

    private sealed class Harness
    {
        public Dependency Dependency { get; } = new();
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Sink? Left { get; set; }
        public Sink? Errors { get; set; }
        public Reply? Reply { get; set; }
        public BufferBlock<FlowMessage>? Captured { get; set; }
        public int SourceRuns { get; set; }
        public int EngineResolutions { get; private set; }
        public int ExpressionCompilations { get; set; }
        public IFlowExpressionEngine Resolve(string name)
        {
            name.ShouldBe("rules");
            EngineResolutions++;
            return new CountingEngine(new TestExpressionEngine(
                ("!message.IsError && above(input, minimum)", context =>
                    !((FlowMessage)context.Variables["message"]!).IsError &&
                    ((FlowValue)context.Variables["input"]!).Json.GetInt32() > Convert.ToInt64(context.Variables["minimum"])),
                ("message.IsError", context => ((FlowMessage)context.Variables["message"]!).IsError),
                ("message.IsError || input == null", context => ((FlowMessage)context.Variables["message"]!).IsError || context.Variables["input"] is null),
                ("message.IsError || payload == null", context => ((FlowMessage)context.Variables["message"]!).IsError || context.Variables["payload"] is null)), this);
        }
    }

    private sealed class CountingEngine(IFlowExpressionEngine inner, Harness harness) : IFlowExpressionEngine
    {
        public string Name => inner.Name;
        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => throw new InvalidOperationException("Conditions must be compiled before execution.");
        public Func<FlowMapContext, T> Compile<T>(string expression, IReadOnlyDictionary<string, Type> parameterTypes)
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
            => await EmitAsync(message.WithValue((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(_dependency.Double(message.Value!.Json.Deserialize<int>()), new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        protected override ValueTask OnDisposeAsync() { _dependency.ComponentDisposals++; return ValueTask.CompletedTask; }
    }

    private sealed class Reply : FlowNode
    {
        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public int DisposeCalls { get; private set; }
        protected override async Task ProcessAsync(FlowMessage message) { Messages.Enqueue(message); await EmitAsync(message); }
        protected override ValueTask OnDisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }

    private sealed class Sink : IFlowNode
    {
        private readonly ActionBlock<FlowMessage> _input;
        public ITargetBlock<FlowMessage> Input => _input;
        public Sink()
        {
            _input = new(message => Messages.Enqueue(message));
        }
        public ConcurrentQueue<FlowMessage> Messages { get; } = new();
        public int DisposeCalls { get; private set; }
        public Task Completion => _input.Completion;
        public void Complete() => _input.Complete();
        public void Fault(Exception error) => ((IDataflowBlock)_input).Fault(error);
        public async ValueTask DisposeAsync() { DisposeCalls++; Complete(); await Completion; }
    }
}
