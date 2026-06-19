using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Mapping;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Routing.Tests;

public sealed class FlowSwitchNodeTests
{
    [Fact]
    public async Task Switch_RoutesMatchedAndDefaultInputs()
    {
        var runtimeNode = CreateNode(
            options => options
                .UseExpressionEngine(new RecordingExpressionEngine(
                    evaluate: (_, context, _) => context.Variables["category"]))
                .RegisterType<InputMessage>("app.input")
                .UseContextFactory(new InputMessageContextFactory()),
            new
            {
                expression = "category",
                inputType = "app.input",
                routes = new[] { "priority" },
                defaultRoute = "other"
            });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var results = new BufferBlock<FlowSwitchResult<InputMessage>>();
        var matched = new BufferBlock<InputMessage>();
        var defaults = new BufferBlock<InputMessage>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Result, results);
        LinkOutput(runtimeNode, RoutingComponentPorts.Matched, matched);
        LinkOutput(runtimeNode, RoutingComponentPorts.Default, defaults);

        await input.Target.SendAsync(new InputMessage("A-100", "priority"));
        await input.Target.SendAsync(new InputMessage("A-101", "standard"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var first = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var second = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        first.RouteKey.ShouldBe("priority");
        first.Matched.ShouldBeTrue();
        first.Value!.Id.ShouldBe("A-100");
        second.RouteKey.ShouldBe("standard");
        second.Matched.ShouldBeFalse();
        second.DefaultRoute.ShouldBe("other");
        (await matched.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Id.ShouldBe("A-100");
        (await defaults.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Id.ShouldBe("A-101");
    }

    [Fact]
    public async Task Switch_TreatsAnyNonEmptyRouteAsMatchedWhenRoutesAreEmpty()
    {
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                evaluate: (_, _, _) => "dynamic")),
            new
            {
                expression = "route"
            });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var results = new BufferBlock<FlowSwitchResult<object>>();
        var matched = new BufferBlock<object>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Result, results);
        LinkOutput(runtimeNode, RoutingComponentPorts.Matched, matched);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Default))!.LinkToDiscard();

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Matched.ShouldBeTrue();
        (await matched.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).ShouldBe("value");
    }

    [Fact]
    public async Task Switch_SupportsCaseInsensitiveRouteMatching()
    {
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                evaluate: (_, _, _) => "PRIORITY")),
            new
            {
                expression = "route",
                routes = new[] { "priority" },
                caseSensitive = false
            });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var results = new BufferBlock<FlowSwitchResult<object>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Result, results);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Matched))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Default))!.LinkToDiscard();

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Matched.ShouldBeTrue();
    }

    [Fact]
    public async Task Switch_CanSuppressRoutedInputs()
    {
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                evaluate: (_, _, _) => "priority")),
            new
            {
                expression = "route",
                routes = new[] { "priority" },
                emitMatchedInput = false,
                emitDefaultInput = false
            });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var results = new BufferBlock<FlowSwitchResult<object>>();
        var matched = new BufferBlock<object>();
        var defaults = new BufferBlock<object>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Result, results);
        LinkOutput(runtimeNode, RoutingComponentPorts.Matched, matched);
        LinkOutput(runtimeNode, RoutingComponentPorts.Default, defaults);

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Matched.ShouldBeTrue();
        matched.TryReceive(out _).ShouldBeFalse();
        defaults.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Switch_EmitsConfiguredRouteOutputPorts()
    {
        var runtimeNode = CreateNode(
            options => options
                .UseExpressionEngine(new RecordingExpressionEngine(
                    evaluate: (_, context, _) => context.Variables["category"]))
                .RegisterType<InputMessage>("app.input")
                .UseContextFactory(new InputMessageContextFactory()),
            new
            {
                expression = "category",
                inputType = "app.input",
                routes = new[] { "priority", "standard" },
                routeOutputs = new Dictionary<string, string>
                {
                    ["priority"] = "Priority",
                    ["standard"] = "Standard"
                }
            });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var priority = new BufferBlock<InputMessage>();
        var standard = new BufferBlock<InputMessage>();
        LinkOutput(runtimeNode, "Priority", priority);
        LinkOutput(runtimeNode, "Standard", standard);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Result))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Matched))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Default))!.LinkToDiscard();

        await input.Target.SendAsync(new InputMessage("A-100", "priority"));
        await input.Target.SendAsync(new InputMessage("A-101", "standard"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await priority.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Id.ShouldBe("A-100");
        (await standard.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Id.ShouldBe("A-101");
    }

    [Fact]
    public async Task Switch_EmitsRouteEnvelopeWhenEnabled()
    {
        var runtimeNode = CreateNode(
            options => options
                .UseExpressionEngine(new RecordingExpressionEngine(
                    evaluate: (_, context, _) => context.Variables["category"]))
                .RegisterType<InputMessage>("app.input")
                .UseContextFactory(new InputMessageContextFactory()),
            new
            {
                expression = "category",
                expressionId = "route-v1",
                expressionName = "route-test",
                inputType = "app.input",
                routes = new[] { "priority" },
                defaultRoute = "other",
                emitRouteEnvelope = true,
                routeOutputs = new Dictionary<string, string>
                {
                    ["priority"] = "Priority"
                }
            });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var routed = new BufferBlock<FlowRoute<InputMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Routed, routed);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Result))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Matched))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Default))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName("Priority"))!.LinkToDiscard();

        await input.Target.SendAsync(new InputMessage("A-100", "priority"));
        await input.Target.SendAsync(new InputMessage("A-101", "standard"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var envelopes = await DrainUntilCompletedAsync(routed);
        envelopes.Count.ShouldBe(2);
        envelopes[0].RouteKey.ShouldBe("priority");
        envelopes[0].Route.ShouldBe("priority");
        envelopes[0].Matched.ShouldBeTrue();
        envelopes[0].DefaultRoute.ShouldBeNull();
        envelopes[0].OutputPort.ShouldBe("Priority");
        envelopes[0].ExpressionId.ShouldBe("route-v1");
        envelopes[0].ExpressionName.ShouldBe("route-test");
        envelopes[0].InputType.ShouldBe("app.input");
        envelopes[0].Value!.Id.ShouldBe("A-100");
        envelopes[1].RouteKey.ShouldBe("standard");
        envelopes[1].Route.ShouldBe("other");
        envelopes[1].Matched.ShouldBeFalse();
        envelopes[1].DefaultRoute.ShouldBe("other");
        envelopes[1].OutputPort.ShouldBeNull();
        envelopes[1].Value!.Id.ShouldBe("A-101");
    }

    [Fact]
    public async Task Switch_CanMapSeveralRoutesToTheSameOutputPort()
    {
        var calls = 0;
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                evaluate: (_, _, _) => ++calls == 1 ? "priority" : "urgent")),
            new
            {
                expression = "category",
                routes = new[] { "priority", "urgent" },
                routeOutputs = new Dictionary<string, string>
                {
                    ["priority"] = "Important",
                    ["urgent"] = "Important"
                }
            });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var important = new BufferBlock<object>();
        LinkOutput(runtimeNode, "Important", important);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Result))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Matched))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Default))!.LinkToDiscard();

        await input.Target.SendAsync(new { category = "priority" });
        await input.Target.SendAsync(new { category = "urgent" });
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await DrainUntilCompletedAsync(important)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Switch_ReportsExpressionFailureAndContinues()
    {
        var calls = 0;
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                evaluate: (_, _, _) =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        throw new InvalidOperationException("switch failed");
                    }

                    return "ok";
                })),
            new
            {
                expression = "route",
                expressionName = "switch-test"
            });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        var errors = new BufferBlock<FlowError>();
        var results = new BufferBlock<FlowSwitchResult<object>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Errors, errors);
        LinkOutput(runtimeNode, RoutingComponentPorts.Result, results);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Matched))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Default))!.LinkToDiscard();

        await input.Target.SendAsync("first");
        await input.Target.SendAsync("second");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        error.Code.ShouldBe(RoutingErrorCodes.SwitchExpressionFailed);
        error.Context!.ShouldContain("expressionName=switch-test");
        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30))).Matched.ShouldBeTrue();
    }

    [Fact]
    public async Task Switch_UsesMostSpecificAssignableContextFactory()
    {
        var runtimeNode = CreateNode(
            options => options
                .UseExpressionEngine(new RecordingExpressionEngine(
                    evaluate: (_, context, _) => context.Variables["route"]))
                .RegisterType<DerivedRouteMessage>("derived-route")
                .UseContextFactory<BaseRouteMessage>(
                    new RouteContextFactory<BaseRouteMessage>("base"))
                .UseContextFactory<DerivedRouteMessage>(
                    new RouteContextFactory<DerivedRouteMessage>("derived")),
            new
            {
                expression = "route",
                inputType = "derived-route",
                routes = new[] { "base", "derived" }
            });
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<DerivedRouteMessage>>();
        var results = new BufferBlock<FlowSwitchResult<DerivedRouteMessage>>();
        LinkOutput(runtimeNode, RoutingComponentPorts.Result, results);
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Matched))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Default))!.LinkToDiscard();

        await input.Target.SendAsync(new DerivedRouteMessage("first"));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        result.RouteKey.ShouldBe("derived");
        result.Matched.ShouldBeTrue();
    }

    [Fact]
    public async Task Switch_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(
            options => options.UseExpressionEngine(new RecordingExpressionEngine(
                evaluate: (_, _, _) => "priority")),
            new
            {
                expression = "route",
                expressionId = "route-v1",
                routes = new[] { "priority" }
            });
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        var input = runtimeNode.FindInput(new PortName(RoutingComponentPorts.Input))
            .ShouldBeOfType<InputPort<object>>();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Result))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Matched))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(RoutingComponentPorts.Default))!.LinkToDiscard();

        await input.Target.SendAsync("value");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        diagnostic.Name.ShouldBe(RoutingDiagnosticNames.SwitchRouted);
        diagnostic.Attributes["routeKey"].ShouldBe("priority");
        diagnostic.Attributes["matched"].ShouldBe(true);
        diagnostic.Attributes["expressionId"].ShouldBe("route-v1");
    }

    [Fact]
    public void Switch_RejectsMissingExpression()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                options => options.UseExpressionEngine(new RecordingExpressionEngine()),
                new { }));

        exception.Message.ShouldContain("expression");
    }

    [Fact]
    public void Switch_RejectsUnknownInputType()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                options => options.UseExpressionEngine(new RecordingExpressionEngine()),
                new
                {
                    expression = "route",
                    inputType = "missing.type"
                }));

        exception.Message.ShouldContain("not registered");
    }

    [Fact]
    public void Switch_RejectsEmptyRouteValues()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                options => options.UseExpressionEngine(new RecordingExpressionEngine()),
                new
                {
                    expression = "route",
                    routes = new[] { "priority", "" }
                }));

        exception.Message.ShouldContain("routes");
    }

    [Fact]
    public void Switch_RejectsRouteOutputForMissingRoute()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                options => options.UseExpressionEngine(new RecordingExpressionEngine()),
                new
                {
                    expression = "route",
                    routes = new[] { "priority" },
                    routeOutputs = new Dictionary<string, string>
                    {
                        ["standard"] = "Standard"
                    }
                }));

        exception.Message.ShouldContain("route output");
    }

    [Fact]
    public void Switch_RejectsRouteOutputWithInvalidPortName()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                options => options.UseExpressionEngine(new RecordingExpressionEngine()),
                new
                {
                    expression = "route",
                    routeOutputs = new Dictionary<string, string>
                    {
                        ["priority"] = "Bad.Port"
                    }
                }));

        exception.Message.ShouldContain("invalid port");
    }

    [Fact]
    public void Switch_RejectsRouteOutputUsingBuiltInPortName()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                options => options.UseExpressionEngine(new RecordingExpressionEngine()),
                new
                {
                    expression = "route",
                    routeOutputs = new Dictionary<string, string>
                    {
                        ["priority"] = RoutingComponentPorts.Result
                    }
                }));

        exception.Message.ShouldContain("built-in port");
    }

    private static RuntimeNode CreateNode(
        Action<Options.RoutingComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterRoutingComponents(configure);
        registry.TryGetFactory(RoutingComponentTypes.Switch, out var factory).ShouldBeTrue();
        return factory(RoutingTestHost.CreateContext(RoutingComponentTypes.Switch, configuration));
    }

    private static void LinkOutput<T>(
        RuntimeNode runtimeNode,
        string port,
        BufferBlock<T> target)
    {
        runtimeNode.FindOutput(new PortName(port))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName(port), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
    }

    private sealed record InputMessage(string Id, string Category);

    private abstract record BaseRouteMessage(string Id);

    private sealed record DerivedRouteMessage(string Id) : BaseRouteMessage(Id);

    private sealed class InputMessageContextFactory : IFlowMapContextFactory<InputMessage>
    {
        public FlowMapContext Create(InputMessage input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["id"] = input.Id,
                    ["category"] = input.Category
                }
            };
    }

    private sealed class RouteContextFactory<TInput>(string route) : IFlowMapContextFactory<TInput>
    {
        public FlowMapContext Create(TInput input)
            => new()
            {
                Variables = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["input"] = input,
                    ["value"] = input,
                    ["route"] = route
                }
            };
    }

    private static async Task<List<T>> DrainUntilCompletedAsync<T>(
        BufferBlock<T> output)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var values = new List<T>();
        while (await output.OutputAvailableAsync(cancellation.Token))
        {
            while (output.TryReceive(out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }
}
