using FluxFlow.Components.Mapping.Contracts;
using FluxFlow.Components.Mapping.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Mapping;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Mapping.Tests;

public sealed class FlowMapperNodeTests
{
    [Fact]
    public async Task MapperNode_MapsObjectInputToObjectOutput()
    {
        var engine = new RecordingExpressionEngine(evaluate: (_, context, _) => $"{context.Variables["input"]}-mapped");
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(engine),
            new
            {
                expression = "map",
                boundedCapacity = 4
            });

        var input = runtimeNode.FindInput(new PortName(MappingComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var output = runtimeNode.FindOutput(new PortName(MappingComponentPorts.Output));
        output.ShouldNotBeNull();
        output.ValueType.ShouldBe(typeof(object));

        var results = new BufferBlock<object>();
        using var link = output.TryLinkTo(
            new InputPort<object>(
                new PortAddress("test", new NodeName("results"), new PortName("Input")),
                results),
            propagateCompletion: true,
            out var error);
        error.ShouldBeNull();
        link.ShouldNotBeNull();

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.ShouldBe("value-mapped");
    }

    [Fact]
    public async Task MapperNode_UsesRegisteredTypesAndContextFactory()
    {
        var engine = new RecordingExpressionEngine(evaluate: (_, context, resultType) =>
        {
            resultType.ShouldBe(typeof(OutputMessage));
            return new OutputMessage((int)context.Variables["mapped"]!);
        });
        var runtimeNode = CreateNode(
            options => options
                .UseExpressionEngine(engine)
                .RegisterType<InputMessage>("app.input")
                .RegisterType<OutputMessage>("app.output")
                .UseContextFactory(new InputMessageContextFactory()),
            new
            {
                expression = "map",
                inputType = "app.input",
                outputType = "app.output"
            });

        var input = runtimeNode.FindInput(new PortName(MappingComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var output = runtimeNode.FindOutput(new PortName(MappingComponentPorts.Output));
        output.ShouldNotBeNull();
        output.ValueType.ShouldBe(typeof(OutputMessage));

        var results = new BufferBlock<OutputMessage>();
        using var link = output.TryLinkTo(
            new InputPort<OutputMessage>(
                new PortAddress("test", new NodeName("results"), new PortName("Input")),
                results),
            propagateCompletion: true,
            out var error);
        error.ShouldBeNull();
        link.ShouldNotBeNull();

        await input.Target.SendAsync(new InputMessage(21));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Value.ShouldBe(42);
    }

    [Fact]
    public async Task MapperNode_UsesConfiguredExpressionEngine()
    {
        var defaultEngine = new RecordingExpressionEngine(
            "default",
            (_, _, _) => "wrong");
        var namedEngine = new RecordingExpressionEngine(
            "named",
            (_, context, _) => $"{context.Variables["input"]}-named");
        var runtimeNode = CreateNode(
            options => options
                .UseExpressionEngine(defaultEngine)
                .UseExpressionEngine(namedEngine, useAsDefault: false),
            new
            {
                expression = "map",
                engine = "named"
            });
        var input = runtimeNode.FindInput(new PortName(MappingComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var results = new BufferBlock<object>();
        runtimeNode.FindOutput(new PortName(MappingComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<object>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out _);

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.ShouldBe("value-named");
    }

    [Fact]
    public async Task MapperNode_ReportsFailureAndContinues()
    {
        var calls = 0;
        var engine = new RecordingExpressionEngine(evaluate: (_, context, _) =>
        {
            calls++;
            if (calls == 1)
            {
                throw new InvalidOperationException("bad expression");
            }

            return $"{context.Variables["input"]}-ok";
        });
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(engine),
            new
            {
                expression = "map",
                expressionName = "test-map"
            });
        var input = runtimeNode.FindInput(new PortName(MappingComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var errors = new BufferBlock<FlowError>();
        var results = new BufferBlock<object>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(MappingComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<object>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out _);

        await input.Target.SendAsync("first");
        await input.Target.SendAsync("second");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MappingErrorCodes.MapperFailed);
        error.Context!.ShouldContain("expressionName=test-map");

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.ShouldBe("second-ok");
    }

    [Fact]
    public async Task MapperNode_ReportsExpectedTypeWhenResultIsIncompatible()
    {
        // The compiled-mapper path casts the engine result to the output type, so a
        // wrong-typed return surfaces as a raw InvalidCastException. The node must
        // still report MapperFailed but with a message naming the expected type.
        var engine = new RecordingExpressionEngine(evaluate: (_, _, _) => "not-an-output-message");
        var runtimeNode = CreateNode(
            options => options
                .UseExpressionEngine(engine)
                .RegisterType<OutputMessage>("app.output"),
            new
            {
                expression = "map",
                outputType = "app.output",
                expressionName = "test-map"
            });
        var input = runtimeNode.FindInput(new PortName(MappingComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var errors = new BufferBlock<FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MappingErrorCodes.MapperFailed);
        error.Message.ShouldContain("incompatible or null value");
        error.Message.ShouldContain(typeof(OutputMessage).ToString());
        error.Message.ShouldContain("app.output");
        error.Exception.ShouldBeOfType<InvalidCastException>();
    }

    [Fact]
    public async Task MapperNode_ReportsExpectedTypeWhenResultIsNull()
    {
        // A null return for a non-nullable value-type output surfaces as a raw
        // NullReferenceException; the node must report the expected type instead.
        var engine = new RecordingExpressionEngine(evaluate: (_, _, _) => null);
        var runtimeNode = CreateNode(
            options => options
                .UseExpressionEngine(engine)
                .RegisterType<int>("app.count"),
            new
            {
                expression = "map",
                outputType = "app.count",
                expressionName = "test-map"
            });
        var input = runtimeNode.FindInput(new PortName(MappingComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var errors = new BufferBlock<FlowError>();
        runtimeNode.Node.Errors.LinkTo(errors);

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MappingErrorCodes.MapperFailed);
        error.Message.ShouldContain("incompatible or null value");
        error.Message.ShouldContain(typeof(int).ToString());
        // A null result for a value-type output surfaces as InvalidCastException or
        // NullReferenceException depending on the cast; both route to the clearer message.
        (error.Exception is InvalidCastException or NullReferenceException).ShouldBeTrue();
    }

    [Fact]
    public async Task MapperNode_ErrorsPortReceivesPerMessageFailures()
    {
        var calls = 0;
        var engine = new RecordingExpressionEngine(evaluate: (_, context, _) =>
        {
            calls++;
            if (calls == 1)
            {
                throw new InvalidOperationException("bad expression");
            }

            return $"{context.Variables["input"]}-ok";
        });
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(engine),
            new
            {
                expression = "map",
                expressionName = "test-map"
            });
        var input = runtimeNode.FindInput(new PortName(MappingComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var errors = new BufferBlock<FlowError>();
        var results = new BufferBlock<object>();
        runtimeNode.FindOutput(new PortName(MappingComponentPorts.Errors))!
            .TryLinkTo(
                new InputPort<FlowError>(
                    new PortAddress("test", new NodeName("errors"), new PortName("Input")),
                    errors),
                propagateCompletion: true,
                out var linkError);
        linkError.ShouldBeNull();
        runtimeNode.FindOutput(new PortName(MappingComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<object>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out _);

        await input.Target.SendAsync("first");
        await input.Target.SendAsync("second");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MappingErrorCodes.MapperFailed);
        error.Context!.ShouldContain("expressionName=test-map");
        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe("second-ok");
    }

    [Fact]
    public async Task MapperNode_FailedPortReceivesDroppedInput()
    {
        var calls = 0;
        var engine = new RecordingExpressionEngine(evaluate: (_, context, _) =>
        {
            calls++;
            if (calls == 1)
            {
                throw new InvalidOperationException("bad expression");
            }

            return $"{context.Variables["input"]}-ok";
        });
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(engine),
            new
            {
                expression = "map",
                expressionName = "test-map"
            });
        var input = runtimeNode.FindInput(new PortName(MappingComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var errors = new BufferBlock<FlowError>();
        var failed = new BufferBlock<object>();
        var results = new BufferBlock<object>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(MappingComponentPorts.Failed))!
            .TryLinkTo(
                new InputPort<object>(
                    new PortAddress("test", new NodeName("failed"), new PortName("Input")),
                    failed),
                propagateCompletion: true,
                out var failedLinkError);
        failedLinkError.ShouldBeNull();
        runtimeNode.FindOutput(new PortName(MappingComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<object>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out _);

        await input.Target.SendAsync("first");
        await input.Target.SendAsync("second");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(MappingErrorCodes.MapperFailed);
        error.Context!.ShouldContain("expressionName=test-map");

        var dropped = await failed.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        dropped.ShouldBe("first");

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.ShouldBe("second-ok");
    }

    [Fact]
    public async Task MapperNode_EmitsDiagnostics()
    {
        var engine = new RecordingExpressionEngine(evaluate: (_, context, _) => context.Variables["input"]);
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(engine),
            new
            {
                expression = "map",
                expressionId = "copy-v1",
                expressionName = "copy"
            });
        var node = runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>();
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        node!.Diagnostics.LinkTo(diagnostics);
        var input = runtimeNode.FindInput(new PortName(MappingComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(MappingDiagnosticNames.MapperSucceeded);
        diagnostic.Attributes["inputType"].ShouldBe("object");
        diagnostic.Attributes["outputType"].ShouldBe("object");
        diagnostic.Attributes["engine"].ShouldBe("test");
        diagnostic.Attributes["expressionId"].ShouldBe("copy-v1");
        diagnostic.Attributes["expressionName"].ShouldBe("copy");
    }

    [Fact]
    public void MapperNode_RejectsMissingExpression()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                options => options.UseExpressionEngine(new RecordingExpressionEngine()),
                new { }));

        exception.Message.ShouldContain("expression");
    }

    [Fact]
    public void MapperNode_RejectsUnknownType()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                options => options.UseExpressionEngine(new RecordingExpressionEngine()),
                new
                {
                    expression = "map",
                    outputType = "missing.output"
                }));

        exception.Message.ShouldContain("missing.output");
    }

    private static RuntimeNode CreateNode(
        Action<Options.MappingComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterMappingComponents(configure);
        registry.TryGetFactory(MappingComponentTypes.Mapper, out var factory).ShouldBeTrue();
        return factory(MappingTestHost.CreateContext(configuration));
    }

    private sealed record InputMessage(int Value);

    private sealed record OutputMessage(int Value);

    private sealed class InputMessageContextFactory : IFlowMapContextFactory<InputMessage>
    {
        public FlowMapContext Create(InputMessage input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["mapped"] = input.Value * 2
                }
            };
    }

    private sealed class RecordingExpressionEngine(
        string name = "test",
        Func<string, FlowMapContext, Type, object?>? evaluate = null)
        : IFlowExpressionEngine
    {
        public string Name => name;

        public object? Evaluate(string expression, FlowMapContext context, Type resultType)
            => evaluate?.Invoke(expression, context, resultType) ?? context.Variables["input"];
    }
}
