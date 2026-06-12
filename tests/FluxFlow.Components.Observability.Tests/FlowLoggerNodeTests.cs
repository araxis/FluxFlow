using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Diagnostics;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Observability.Tests;

public sealed class FlowLoggerNodeTests
{
    [Fact]
    public async Task Logger_EmitsStructuredEntry()
    {
        var timestamp = new DateTimeOffset(2026, 6, 2, 18, 30, 0, TimeSpan.Zero);
        var clock = new RecordingObservabilityClock(timestamp);
        var runtimeNode = CreateNode(
            options => options
                .UseClock(clock)
                .RegisterType<InputMessage>("message")
                .UseValueSelector<InputMessage>("kind", (message, _) => message.Kind)
                .UseValueSelector<InputMessage>("size", (message, _) => message.Payload.Length),
            new
            {
                inputType = "message",
                level = "Warning",
                category = "workflow.test",
                messageTemplate = "Observed {kind}:{size} #{sequence}",
                attributeSelectors = new[] { "kind", "size" }
            });
        var input = runtimeNode.FindInput(new PortName(ObservabilityComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var entries = new BufferBlock<FlowLogEntry>();
        LinkEntries(runtimeNode, entries);

        await input.Target.SendAsync(new InputMessage("alpha", [1, 2, 3], true));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var entry = await entries.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        entry.Level.ShouldBe(FlowLogLevel.Warning);
        entry.Category.ShouldBe("workflow.test");
        entry.Message.ShouldBe("Observed alpha:3 #1");
        entry.InputType.ShouldBe("message");
        entry.Sequence.ShouldBe(1);
        entry.Timestamp.ShouldBe(timestamp);
        entry.Attributes["kind"].ShouldBe("alpha");
        entry.Attributes["size"].ShouldBe(3);
    }

    [Fact]
    public async Task Logger_ReportsAttributeSelectorFailureAndStillEmitsEntry()
    {
        var runtimeNode = CreateNode(
            options => options
                .RegisterType<InputMessage>("message")
                .UseValueSelector<InputMessage>("kind", (message, _) => message.Kind)
                .UseValueSelector<InputMessage>("broken", (_, _) => throw new InvalidOperationException("select failed")),
            new
            {
                inputType = "message",
                attributeSelectors = new[] { "kind", "broken" }
            });
        var input = runtimeNode.FindInput(new PortName(ObservabilityComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var errors = new BufferBlock<FlowError>();
        var entries = new BufferBlock<FlowLogEntry>();
        runtimeNode.Node.Errors.LinkTo(errors);
        LinkEntries(runtimeNode, entries);

        await input.Target.SendAsync(new InputMessage("alpha", [1], true));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(ObservabilityErrorCodes.LoggerAttributeSelectorFailed);
        error.Context!.ShouldContain("selector=broken");
        var entry = await entries.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        entry.Attributes["kind"].ShouldBe("alpha");
        entry.Attributes.ContainsKey("broken").ShouldBeFalse();
    }

    [Fact]
    public async Task Logger_ExposesErrorsPortAndDeliversSelectorFailures()
    {
        var runtimeNode = CreateNode(
            options => options
                .RegisterType<InputMessage>("message")
                .UseValueSelector<InputMessage>("broken", (_, _) => throw new InvalidOperationException("select failed")),
            new
            {
                inputType = "message",
                attributeSelectors = new[] { "broken" }
            });
        var input = runtimeNode.FindInput(new PortName(ObservabilityComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var errorsPort = runtimeNode.FindOutput(new PortName(ObservabilityComponentPorts.Errors));
        errorsPort.ShouldNotBeNull();
        var errors = new BufferBlock<FlowError>();
        errorsPort.TryLinkTo(
            new InputPort<FlowError>(
                new PortAddress("test", new NodeName("errors"), new PortName("Input")),
                errors),
            propagateCompletion: true,
            out var linkError);
        linkError.ShouldBeNull();
        LinkEntries(runtimeNode, new BufferBlock<FlowLogEntry>());

        await input.Target.SendAsync(new InputMessage("alpha", [1], true));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(ObservabilityErrorCodes.LoggerAttributeSelectorFailed);
    }

    [Fact]
    public async Task Logger_DoesNotExpandPlaceholdersFromSubstitutedValues()
    {
        var runtimeNode = CreateNode(
            options => options
                .RegisterType<InputMessage>("message")
                .UseValueSelector<InputMessage>("kind", (message, _) => message.Kind),
            new
            {
                inputType = "message",
                messageTemplate = "Observed {kind}",
                attributeSelectors = new[] { "kind" }
            });
        var input = runtimeNode.FindInput(new PortName(ObservabilityComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var entries = new BufferBlock<FlowLogEntry>();
        LinkEntries(runtimeNode, entries);

        await input.Target.SendAsync(new InputMessage("{category}", [1], true));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var entry = await entries.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        entry.Message.ShouldBe("Observed {category}");
    }

    [Fact]
    public async Task Logger_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "string",
                category = "workflow.test"
            });
        var input = runtimeNode.FindInput(new PortName(ObservabilityComponentPorts.Input))
            .ShouldBeOfType<InputPort<string>>();
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        runtimeNode.FindOutput(new PortName(ObservabilityComponentPorts.Entries))!
            .LinkToDiscard();

        await input.Target.SendAsync("hello");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(ObservabilityDiagnosticNames.LoggerEmitted);
        diagnostic.Attributes["name"].ShouldBe("workflow.test");
    }

    [Fact]
    public void Logger_RejectsUnsupportedLevel()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    inputType = "string",
                    level = "Nope"
                }));

        exception.Message.ShouldContain("level");
    }

    [Fact]
    public async Task Logger_TreatsNullAttributeSelectorsAsEmpty()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                inputType = "string",
                attributeSelectors = (string[]?)null
            });
        var input = runtimeNode.FindInput(new PortName(ObservabilityComponentPorts.Input))
            .ShouldBeOfType<InputPort<string>>();
        var entries = new BufferBlock<FlowLogEntry>();
        LinkEntries(runtimeNode, entries);

        await input.Target.SendAsync("hello");
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var entry = await entries.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        entry.Attributes.ShouldBeEmpty();
    }

    private static RuntimeNode CreateNode(
        Action<ObservabilityComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterObservabilityComponents(configure);
        registry.TryGetFactory(ObservabilityComponentTypes.Logger, out var factory).ShouldBeTrue();
        return factory(ObservabilityTestHost.CreateContext(
            ObservabilityComponentTypes.Logger,
            configuration));
    }

    private static void LinkEntries(
        RuntimeNode runtimeNode,
        BufferBlock<FlowLogEntry> target)
    {
        runtimeNode.FindOutput(new PortName(ObservabilityComponentPorts.Entries))!
            .TryLinkTo(
                new InputPort<FlowLogEntry>(
                    new PortAddress("test", new NodeName("entries"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
    }

    private sealed record InputMessage(string Kind, byte[] Payload, bool Enabled);
}
