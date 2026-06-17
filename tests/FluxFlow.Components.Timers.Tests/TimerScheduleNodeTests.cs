using FluxFlow.Components.Timers.Contracts;
using FluxFlow.Components.Timers.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Timers.Tests;

public sealed class TimerScheduleNodeTests
{
    [Fact]
    public async Task Schedule_EmitsConfiguredTickCount()
    {
        var runtimeNode = CreateNode(new
        {
            name = "cron",
            cron = "* * * * * *",
            maxTicks = 2,
            boundedCapacity = 4
        });
        var output = new BufferBlock<ScheduleTick>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        var ticks = await DrainUntilCompletedAsync(output);
        ticks.Select(tick => tick.Sequence).ShouldBe([1, 2]);
        ticks.ShouldAllBe(tick => tick.Name == "cron");
        ticks.ShouldAllBe(tick => tick.Cron == "* * * * * *");
        ticks.ShouldAllBe(tick => tick.TimeZoneId == TimeZoneInfo.Utc.Id);
    }

    [Fact]
    public async Task Schedule_EmitsDiagnosticsAndEvents()
    {
        var runtimeNode = CreateNode(new
        {
            name = "diag",
            expression = "* * * * * *",
            maxTicks = 1
        });
        var output = new BufferBlock<ScheduleTick>();
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        var events = new BufferBlock<FlowEvent>();
        LinkOutput(runtimeNode, output);
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(
                diagnostics,
                new DataflowLinkOptions { PropagateCompletion = true });
        runtimeNode.Node.ShouldBeAssignableTo<IFlowEventSource>()!
            .Events.LinkTo(
                events,
                new DataflowLinkOptions { PropagateCompletion = true });

        await runtimeNode.Node.StartAsync().WaitAsync(TimeSpan.FromSeconds(30));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));

        (await DrainUntilCompletedAsync(output)).ShouldHaveSingleItem();
        var diagnosticNames = (await DrainDiagnosticsUntilCompletedAsync(diagnostics))
            .Select(diagnostic => diagnostic.Name)
            .ToArray();
        diagnosticNames.ShouldContain(TimerDiagnosticNames.ScheduleStarted);
        diagnosticNames.ShouldContain(TimerDiagnosticNames.ScheduleTick);
        diagnosticNames.ShouldContain(TimerDiagnosticNames.ScheduleStopped);
        var flowEvent = await events.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(30));
        flowEvent.Type.ShouldBe(TimerEventNames.ScheduleTick);
        flowEvent.Subject.ShouldBe("diag");
    }

    [Fact]
    public async Task Schedule_DisposeBeforeStartCompletesOutput()
    {
        var runtimeNode = CreateNode(new
        {
            cron = "* * * * * *"
        });
        var output = new BufferBlock<ScheduleTick>();
        LinkOutput(runtimeNode, output);

        await runtimeNode.Node.ShouldBeAssignableTo<IAsyncDisposable>()!
            .DisposeAsync();

        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(30));
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Schedule_AcceptsNamesAndQuestionMarks()
    {
        var runtimeNode = CreateNode(new
        {
            cron = "0 0 12 ? JAN MON",
            maxTicks = 1
        });

        runtimeNode.FindOutput(new PortName(TimerComponentPorts.Output)).ShouldNotBeNull();
    }

    [Fact]
    public void Schedule_RejectsMissingCron()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { }));

        exception.Message.ShouldContain("cron");
    }

    [Fact]
    public void Schedule_RejectsInvalidCron()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { cron = "* * *" }));

        exception.Message.ShouldContain("cron");
    }

    [Fact]
    public void Schedule_RejectsDuplicateCronOptions()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                cron = "* * * * * *",
                expression = "* * * * * *"
            }));

        exception.Message.ShouldContain("cron");
    }

    [Fact]
    public void Schedule_RejectsInvalidTimeZone()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                cron = "* * * * * *",
                timeZoneId = "Missing/Zone"
            }));

        exception.Message.ShouldContain("timeZoneId");
    }

    [Fact]
    public void Schedule_RejectsInvalidMaxTicks()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                cron = "* * * * * *",
                maxTicks = 0
            }));

        exception.Message.ShouldContain("maxTicks");
    }

    [Fact]
    public void Schedule_RejectsInvalidBoundedCapacity()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new
            {
                cron = "* * * * * *",
                boundedCapacity = 0
            }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    private static RuntimeNode CreateNode(object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterTimerComponents();
        registry.TryGetFactory(TimerComponentTypes.Schedule, out var factory).ShouldBeTrue();
        return factory(TimerTestHost.CreateContext(
            TimerComponentTypes.Schedule,
            configuration,
            "schedule"));
    }

    private static void LinkOutput(
        RuntimeNode runtimeNode,
        BufferBlock<ScheduleTick> target)
    {
        runtimeNode.FindOutput(new PortName(TimerComponentPorts.Output))!
            .TryLinkTo(
                new InputPort<ScheduleTick>(
                    new PortAddress("test", new NodeName("ticks"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
    }

    private static async Task<List<ScheduleTick>> DrainUntilCompletedAsync(
        BufferBlock<ScheduleTick> output)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ticks = new List<ScheduleTick>();
        while (await output.OutputAvailableAsync(cancellation.Token))
        {
            while (output.TryReceive(out var tick))
            {
                ticks.Add(tick);
            }
        }

        return ticks;
    }

    private static async Task<List<FlowDiagnostic>> DrainDiagnosticsUntilCompletedAsync(
        BufferBlock<FlowDiagnostic> diagnostics)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
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
}
