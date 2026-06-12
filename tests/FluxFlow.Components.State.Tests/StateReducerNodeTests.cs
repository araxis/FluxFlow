using FluxFlow.Components.State.Contracts;
using FluxFlow.Components.State.Diagnostics;
using FluxFlow.Components.State.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.State.Tests;

public sealed class StateReducerNodeTests
{
    [Fact]
    public async Task Reducer_UpdatesStatePerKeyInOrder()
    {
        var runtimeNode = CreateNode(
            new
            {
                reducer = "count"
            },
            new SampleExpressionEngine());
        var input = GetInput(runtimeNode);
        var output = LinkOutput<StateReducerResult>(runtimeNode);

        await input.Target.SendAsync(new StateReducerInput { Key = "a", Input = "first" });
        await input.Target.SendAsync(new StateReducerInput { Key = "a", Input = "second" });
        await input.Target.SendAsync(new StateReducerInput { Key = "b", Input = "other" });
        input.Target.Complete();

        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var third = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        first.Key.ShouldBe("a");
        first.PreviousState.ShouldBeNull();
        first.NewState.ShouldBe(1);
        first.Version.ShouldBe(1);
        second.Key.ShouldBe("a");
        second.PreviousState.ShouldBe(1);
        second.NewState.ShouldBe(2);
        second.Version.ShouldBe(2);
        third.Key.ShouldBe("b");
        third.NewState.ShouldBe(1);
        third.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Reducer_UsesInitialStateFromRequestOrOptions()
    {
        var runtimeNode = CreateNode(
            new
            {
                reducer = "count",
                initialState = 10
            },
            new SampleExpressionEngine());
        var input = GetInput(runtimeNode);
        var output = LinkOutput<StateReducerResult>(runtimeNode);

        await input.Target.SendAsync(new StateReducerInput { Key = "a", Input = "first" });
        await input.Target.SendAsync(new StateReducerInput { Key = "b", InitialState = 20 });
        input.Target.Complete();

        var first = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        ReadNumber(first.PreviousState).ShouldBe(10);
        first.NewState.ShouldBe(11);
        second.PreviousState.ShouldBe(20);
        second.NewState.ShouldBe(21);
    }

    [Fact]
    public async Task Reducer_CanResolveKeyWithExpression()
    {
        var runtimeNode = CreateNode(
            new
            {
                keyExpression = "topic-key",
                reducer = "last-input"
            },
            new SampleExpressionEngine());
        var input = GetInput(runtimeNode);
        var output = LinkOutput<StateReducerResult>(runtimeNode);

        await input.Target.SendAsync(new StateReducerInput
        {
            Key = "ignored",
            Input = "payload",
            Variables = new Dictionary<string, object?>
            {
                ["topic"] = "orders/created"
            }
        });
        input.Target.Complete();

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Key.ShouldBe("orders/created");
        result.NewState.ShouldBe("payload");
    }

    [Fact]
    public async Task Reducer_ResetAndClearUseSameInput()
    {
        var runtimeNode = CreateNode(
            new
            {
                reducer = "count",
                initialState = 5
            },
            new SampleExpressionEngine());
        var input = GetInput(runtimeNode);
        var output = LinkOutput<StateReducerResult>(runtimeNode);

        await input.Target.SendAsync(new StateReducerInput { Key = "a" });
        await input.Target.SendAsync(new StateReducerInput
        {
            Key = "a",
            InitialState = 100,
            Operation = StateReducerOperation.Reset
        });
        await input.Target.SendAsync(new StateReducerInput
        {
            Key = "a",
            Operation = StateReducerOperation.Clear
        });
        await input.Target.SendAsync(new StateReducerInput { Key = "a" });
        input.Target.Complete();

        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var reset = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var clear = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var afterClear = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        reset.PreviousState.ShouldBe(6);
        reset.NewState.ShouldBe(100);
        reset.Version.ShouldBe(2);
        clear.PreviousState.ShouldBe(100);
        clear.NewState.ShouldBeNull();
        clear.Version.ShouldBe(3);
        ReadNumber(afterClear.PreviousState).ShouldBe(5);
        afterClear.NewState.ShouldBe(6);
        afterClear.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Reducer_UsesConfiguredClockForResults()
    {
        var reduceAt = new DateTimeOffset(2026, 6, 2, 18, 45, 0, TimeSpan.Zero);
        var resetAt = reduceAt.AddSeconds(1);
        var clearAt = resetAt.AddSeconds(1);
        var clock = new RecordingStateClock(reduceAt, resetAt, clearAt);
        var runtimeNode = CreateNode(
            new
            {
                reducer = "count",
                initialState = 5
            },
            new SampleExpressionEngine(),
            options => options.UseClock(clock));
        var input = GetInput(runtimeNode);
        var output = LinkOutput<StateReducerResult>(runtimeNode);

        await input.Target.SendAsync(new StateReducerInput { Key = "a" });
        await input.Target.SendAsync(new StateReducerInput
        {
            Key = "a",
            InitialState = 100,
            Operation = StateReducerOperation.Reset
        });
        await input.Target.SendAsync(new StateReducerInput
        {
            Key = "a",
            Operation = StateReducerOperation.Clear
        });
        input.Target.Complete();

        var reduce = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var reset = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var clear = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        reduce.UpdatedAt.ShouldBe(reduceAt);
        reset.UpdatedAt.ShouldBe(resetAt);
        clear.UpdatedAt.ShouldBe(clearAt);
    }

    [Fact]
    public async Task Reducer_ReportsReducerFailuresAndContinues()
    {
        var runtimeNode = CreateNode(
            new
            {
                reducer = "fail-on-bad"
            },
            new SampleExpressionEngine());
        var input = GetInput(runtimeNode);
        var output = LinkOutput<StateReducerResult>(runtimeNode);
        var errors = LinkOutput<FlowError>(runtimeNode, StateComponentPorts.Errors);

        await input.Target.SendAsync(new StateReducerInput { Key = "a", Input = "bad" });
        await input.Target.SendAsync(new StateReducerInput { Key = "a", Input = "good" });
        input.Target.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(StateErrorCodes.ReducerFailed);
        result.NewState.ShouldBe("good");
        result.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Reducer_RespectsMaxKeyLimit()
    {
        var runtimeNode = CreateNode(
            new
            {
                reducer = "count",
                maxKeys = 1
            },
            new SampleExpressionEngine());
        var input = GetInput(runtimeNode);
        var output = LinkOutput<StateReducerResult>(runtimeNode);
        var errors = LinkOutput<FlowError>(runtimeNode, StateComponentPorts.Errors);

        await input.Target.SendAsync(new StateReducerInput { Key = "a" });
        await input.Target.SendAsync(new StateReducerInput { Key = "b" });
        input.Target.Complete();

        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Key.ShouldBe("a");
        error.Code.ShouldBe(StateErrorCodes.KeyLimitReached);
    }

    [Fact]
    public async Task Reducer_CapsItemizedRejectedKeyDiagnostics()
    {
        var runtimeNode = CreateNode(
            new
            {
                reducer = "count",
                maxKeys = 1
            },
            new SampleExpressionEngine());
        var input = GetInput(runtimeNode);
        var output = LinkOutput<StateReducerResult>(runtimeNode);
        var errors = LinkOutput<FlowError>(runtimeNode, StateComponentPorts.Errors);
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(
                diagnostics,
                new DataflowLinkOptions { PropagateCompletion = true });

        await input.Target.SendAsync(new StateReducerInput { Key = "tracked" });
        for (var index = 0; index < 1100; index++)
        {
            await input.Target.SendAsync(new StateReducerInput { Key = $"rejected-{index}" });
        }

        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Key.ShouldBe("tracked");
        var keyLimitDiagnostics = (await DrainDiagnosticsUntilCompletedAsync(diagnostics))
            .Where(diagnostic => diagnostic.Name == StateDiagnosticNames.KeyLimitReached)
            .ToList();
        keyLimitDiagnostics.Count.ShouldBe(1025);
        keyLimitDiagnostics
            .Count(diagnostic => diagnostic.Message!.Contains("will not be itemized"))
            .ShouldBe(1);
        (await DrainErrorsUntilCompletedAsync(errors)).Count.ShouldBe(1100);
    }

    [Fact]
    public async Task Reducer_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(
            new
            {
                reducer = "count",
                expressionName = "counter"
            },
            new SampleExpressionEngine());
        var input = GetInput(runtimeNode);
        var output = LinkOutput<StateReducerResult>(runtimeNode);
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(
                diagnostics,
                new DataflowLinkOptions { PropagateCompletion = true });

        await input.Target.SendAsync(new StateReducerInput { Key = "a" });
        input.Target.Complete();
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(StateDiagnosticNames.ReducerUpdated);
        diagnostic.Attributes["key"].ShouldBe("a");
        diagnostic.Attributes["version"].ShouldBe(1L);
    }

    [Fact]
    public void Reducer_RejectsInvalidOptions()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { reducer = "", boundedCapacity = 1 }, new SampleExpressionEngine()));

        exception.Message.ShouldContain("reducer");
    }

    [Fact]
    public void Reducer_RequiresExpressionEngine()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterStateComponents();
        registry.TryGetFactory(StateComponentTypes.Reducer, out var factory).ShouldBeTrue();

        var exception = Should.Throw<InvalidOperationException>(
            () => factory(CreateContext(new { reducer = "count" })));

        exception.Message.ShouldContain("expression engine");
    }

    [Fact]
    public async Task Reducer_UsesExpressionEngineResolver()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterStateComponents(options => options.UseExpressionEngineResolver(name =>
            {
                name.ShouldBe("custom");
                return new SampleExpressionEngine();
            }));
        registry.TryGetFactory(StateComponentTypes.Reducer, out var factory).ShouldBeTrue();
        var runtimeNode = factory(CreateContext(new
        {
            reducer = "count",
            engine = "custom"
        }));
        var input = GetInput(runtimeNode);
        var output = LinkOutput<StateReducerResult>(runtimeNode);

        await input.Target.SendAsync(new StateReducerInput { Key = "a" });
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.NewState.ShouldBe(1);
    }

    [Fact]
    public void RegisterStateComponents_RegistersReducer()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterStateComponents(options => options.UseExpressionEngine(new SampleExpressionEngine()));

        registry.TryGetFactory(StateComponentTypes.Reducer, out _).ShouldBeTrue();
    }

    private static RuntimeNode CreateNode(
        object configuration,
        IFlowExpressionEngine expressionEngine,
        Action<StateComponentOptions>? configure = null)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterStateComponents(options =>
            {
                options.UseExpressionEngine(expressionEngine);
                configure?.Invoke(options);
            });
        registry.TryGetFactory(StateComponentTypes.Reducer, out var factory).ShouldBeTrue();
        return factory(CreateContext(configuration));
    }

    private static RuntimeNodeFactoryContext CreateContext(object configuration)
    {
        var root = JsonSerializer.SerializeToElement(configuration);
        var values = root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        return new RuntimeNodeFactoryContext(
            new NodeName("state"),
            new NodeDefinition
            {
                Type = StateComponentTypes.Reducer,
                Configuration = values
            },
            "main",
            new Dictionary<NodeName, RuntimeNode>());
    }

    private static InputPort<StateReducerInput> GetInput(RuntimeNode runtimeNode)
        => runtimeNode.FindInput(new PortName(StateComponentPorts.Input))
            .ShouldBeOfType<InputPort<StateReducerInput>>();

    private static BufferBlock<T> LinkOutput<T>(
        RuntimeNode runtimeNode,
        string portName = StateComponentPorts.Output)
    {
        var target = new BufferBlock<T>();
        runtimeNode.FindOutput(new PortName(portName))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName("items"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
        return target;
    }

    private static async Task<List<FlowDiagnostic>> DrainDiagnosticsUntilCompletedAsync(
        BufferBlock<FlowDiagnostic> diagnostics)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entries = new List<FlowDiagnostic>();
        while (await diagnostics.OutputAvailableAsync(cancellation.Token))
        {
            while (diagnostics.TryReceive(out var entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static async Task<List<FlowError>> DrainErrorsUntilCompletedAsync(
        BufferBlock<FlowError> errors)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var entries = new List<FlowError>();
        while (await errors.OutputAvailableAsync(cancellation.Token))
        {
            while (errors.TryReceive(out var entry))
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private static long ReadNumber(object? value)
        => value switch
        {
            long number => number,
            int number => number,
            JsonElement json when json.ValueKind == JsonValueKind.Number &&
                                  json.TryGetInt64(out var number) => number,
            _ => throw new InvalidOperationException(
                $"Cannot read '{value?.GetType().Name ?? "null"}' as a number.")
        };

    private sealed class SampleExpressionEngine : IFlowExpressionEngine
    {
        public string Name => "sample";

        public object? Evaluate(
            string expression,
            FlowMapContext context,
            Type resultType)
            => expression switch
            {
                "count" => CoerceNumber(context.Variables["state"]) + 1,
                "last-input" => context.Variables["input"],
                "topic-key" => context.Variables["topic"],
                "fail-on-bad" when Equals(context.Variables["input"], "bad") =>
                    throw new InvalidOperationException("bad input"),
                "fail-on-bad" => context.Variables["input"],
                _ => throw new InvalidOperationException($"Unknown expression '{expression}'.")
            };

        private static long CoerceNumber(object? value)
            => value switch
            {
                null => 0,
                long number => number,
                int number => number,
                JsonElement json when json.ValueKind == JsonValueKind.Number &&
                                      json.TryGetInt64(out var number) => number,
                _ => throw new InvalidOperationException(
                    $"Cannot coerce '{value.GetType().Name}' to a number.")
            };
    }
}
