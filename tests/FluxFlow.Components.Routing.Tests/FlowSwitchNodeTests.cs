using FluxFlow.Components.Routing.Contracts;
using FluxFlow.Components.Routing.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Mapping;
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
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var first = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var second = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        first.RouteKey.ShouldBe("priority");
        first.Matched.ShouldBeTrue();
        first.Value!.Id.ShouldBe("A-100");
        second.RouteKey.ShouldBe("standard");
        second.Matched.ShouldBeFalse();
        second.DefaultRoute.ShouldBe("other");
        (await matched.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Id.ShouldBe("A-100");
        (await defaults.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Id.ShouldBe("A-101");
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
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Matched.ShouldBeTrue();
        (await matched.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe("value");
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
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Matched.ShouldBeTrue();
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
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Matched.ShouldBeTrue();
        matched.TryReceive(out _).ShouldBeFalse();
        defaults.TryReceive(out _).ShouldBeFalse();
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
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(RoutingErrorCodes.SwitchExpressionFailed);
        error.Context!.ShouldContain("expressionName=switch-test");
        (await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Matched.ShouldBeTrue();
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
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
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
}
