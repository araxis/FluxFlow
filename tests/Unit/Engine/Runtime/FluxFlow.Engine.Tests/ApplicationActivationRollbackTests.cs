using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.Definition;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationActivationRollbackTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_source_start_preserves_active_revision_and_routing(bool cancel)
    {
        var activeEcho = new PrefixNode("active:");
        var activeGuard = new StartupGuard();
        var initial = Definition(activeEcho, activeGuard);
        var services = new ServiceCollection();
        services.AddFluxFlowUpdates(initial, options => options.StartWithHost = false);
        await using var provider = services.BuildServiceProvider();
        var application = provider.GetRequiredService<FluxFlowApplication>();
        (await application.StartAsync()).IsApplied.ShouldBeTrue();
        var snapshot = application.Current;
        var ports = application.Ports;
        var portRevision = ports.CurrentRevision;
        await AssertReply("before");

        using var cancellation = new CancellationTokenSource();
        var candidateEcho = new PrefixNode("candidate:");
        var failure = new InvalidOperationException("source startup failed");
        var candidateGuard = new StartupGuard(token =>
        {
            if (!cancel) return Task.FromException(failure);
            cancellation.Cancel();
            return Task.FromCanceled(token);
        });
        var operation = application.ApplyAsync("candidate", Definition(candidateEcho, candidateGuard), cancellation.Token).AsTask();
        if (cancel)
            (await Should.ThrowAsync<OperationCanceledException>(() => operation.WaitAsync(Deadline)))
                .CancellationToken.ShouldBe(cancellation.Token);
        else
        {
            var result = await operation.WaitAsync(Deadline);
            result.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
            result.Diagnostics.ShouldContain(diagnostic => diagnostic.Stage == ApplicationUpdateStage.Activation &&
                diagnostic.Error.Code == "revision.activation.failed");
        }

        application.Current.ShouldBeSameAs(snapshot);
        application.State.ShouldBe(ApplicationState.Running);
        application.Ports.ShouldBeSameAs(ports);
        ports.CurrentRevision.ShouldBeSameAs(portRevision);
        candidateGuard.StartCalls.ShouldBe(1);
        candidateEcho.DisposeCalls.ShouldBe(1);
        candidateGuard.DisposeCalls.ShouldBe(1);
        activeEcho.DisposeCalls.ShouldBe(0);
        activeGuard.DisposeCalls.ShouldBe(0);
        await AssertReply("after");

        await application.StopAsync().AsTask().WaitAsync(Deadline);
        activeEcho.DisposeCalls.ShouldBe(1);
        activeGuard.DisposeCalls.ShouldBe(1);
        candidateEcho.DisposeCalls.ShouldBe(1);
        candidateGuard.DisposeCalls.ShouldBe(1);

        async Task AssertReply(string value)
        {
            var request = FlowMessage.Create(value);
            var result = await ports.SendAndReceiveAsync<string, string>("main.echo.Input", "main.echo.Output", request, Deadline);
            result.Status.ShouldBe(PortRequestStatus.Received);
            result.Response.ShouldNotBeNull().Value.ShouldBe($"active:{value}");
            result.Response.TraceId.ShouldBe(request.TraceId);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Startup_output_is_published_only_after_successful_commit(bool reject, bool cancel)
    {
        var address = ApplicationAddress.Parse("main.source.Output");
        await using var ports = new ApplicationPortRuntimeBuilder()
            .AddOutput(address, capacity: 4, discardUnmatched: true).Build();
        var initialSource = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 4 });
        await using var initialBuilder = ports.CreateRevision("initial");
        await using var initialRevision = initialBuilder.AttachOutput(address, initialSource).Build();
        await using var initialLease = await initialRevision.ActivateAsync();
        var initialInfo = ports.CurrentRevision;
        await using var observation = (await ports.ObserveAsync<FlowValue>(address, capacity: 4))
            .Observation.ShouldNotBeNull();
        var observed = new BufferBlock<FlowMessage<FlowValue>>(new DataflowBlockOptions { BoundedCapacity = 4 });
        using var observationLink = observation.Messages.LinkTo(observed, new DataflowLinkOptions { PropagateCompletion = true });
        var source = new BufferBlock<FlowMessage>(new DataflowBlockOptions { BoundedCapacity = 4 });
        var messages = new[] { "first", "second", "third" }.Select(value => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToArray();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("source startup failed");
        var guard = new StartupGuard(async token =>
        {
            foreach (var message in messages)
                (await source.SendAsync(message, token)).ShouldBeTrue();
            entered.SetResult();
            await release.Task.WaitAsync(token);
            if (cancel) cancellation.Cancel(); // Startup returns normally; commit must still reject cancellation.
            else if (reject) throw failure;
        });
        await using var builder = ports.CreateRevision("candidate");
        await using var candidate = new ApplicationRuntimeRevisionCandidate([new(guard)],
            builder.AttachOutput(address, source).Build(), [], null, () => ValueTask.CompletedTask, null);
        var activation = candidate.ActivateAsync(cancellation.Token).AsTask();
        try
        {
            await entered.Task.WaitAsync(Deadline);
            activation.IsCompleted.ShouldBeFalse();
            ports.CurrentRevision.ShouldBeSameAs(initialInfo);
            observed.TryReceive(out _).ShouldBeFalse();
            release.SetResult();
            if (cancel)
                (await Should.ThrowAsync<OperationCanceledException>(() => activation.WaitAsync(Deadline)))
                    .CancellationToken.ShouldBe(cancellation.Token);
            else if (reject)
                (await Should.ThrowAsync<InvalidOperationException>(() => activation.WaitAsync(Deadline)))
                    .ShouldBeSameAs(failure);
            else
            {
                await activation.WaitAsync(Deadline);
                ports.CurrentRevision.ShouldNotBeNull().RevisionId.ShouldBe("candidate");
                ports.CurrentRevision.Sequence.ShouldBe(initialInfo!.Sequence + 1);
                foreach (var message in messages)
                {
                    var received = await observed.ReceiveAsync(Deadline);
                    received.Value.ShouldBeSameAs(message.Value);
                    received.TraceId.ShouldBe(message.TraceId);
                }
                source.Complete();
                await candidate.DrainAsync().AsTask().WaitAsync(Deadline);
            }
            if (reject)
            {
                ports.CurrentRevision.ShouldBeSameAs(initialInfo);
                guard.FaultCalls.ShouldBe(1);
            }
            else guard.FaultCalls.ShouldBe(0);
            await candidate.DisposeAsync().AsTask().WaitAsync(Deadline);
            guard.StartCalls.ShouldBe(1);
            guard.DisposeCalls.ShouldBe(1);
            initialSource.Complete();
            await initialLease.DrainOutputsAsync(CancellationToken.None).AsTask().WaitAsync(Deadline);
            ports.Complete();
            await ports.Completion.WaitAsync(Deadline);
            (await observed.OutputAvailableAsync().WaitAsync(Deadline)).ShouldBeFalse();
            await observed.Completion.WaitAsync(Deadline);
        }
        finally
        {
            release.TrySetResult();
            ((IDataflowBlock)source).Fault(new OperationCanceledException("test cleanup"));
        }
    }

    private static ApplicationSetup Definition(PrefixNode echo, StartupGuard guard)
    {
        var builder = new ApplicationDefinitionBuilder();
        var workflow = builder.AddWorkflow("main");
        var route = workflow.AddComponent("echo", "test.rollback.echo", context => { context.HasInput(echo.Input); context.HasOutput(echo.Output); context.HasEvents(echo.Events); return ValueTask.FromResult<IFlowNode>(echo); }, static _ => { }, static component => new InputOutputComponentHandle<FlowValue, FlowValue>(component, "Input", "Output", "Events"));
        workflow.AddComponent("guard", "test.rollback.guard", context =>
        {
            var createdNode = guard;

            return ValueTask.FromResult<IFlowNode>(createdNode);
        }, static _ => { }, static component => new GuardHandle(component));
        return builder.Build();
    }

    private sealed class GuardHandle(ComponentHandle definition) : AuthoredComponentHandle(definition);

    private sealed class PrefixNode(string prefix) : FlowNode
    {
        public int DisposeCalls { get; private set; }
        protected override async Task ProcessAsync(FlowMessage message)
            => await EmitAsync(message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(prefix + message.Value!.Json.Deserialize<string>(), new JsonSerializerOptions(JsonSerializerDefaults.Web)))), Stopping);
        protected override ValueTask OnDisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }

    private sealed class StartupGuard(Func<CancellationToken, Task>? start = null) : IFlowComponent, IFlowSource
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FlowPorts Ports { get; } = new();
        public Task Completion => _completion.Task;
        public int StartCalls { get; private set; }
        public int FaultCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCalls++;
            return start?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }
        public void Complete() => _completion.TrySetResult();
        public void Fault(Exception exception) { FaultCalls++; _completion.TrySetException(exception); }
        public ValueTask DisposeAsync() { DisposeCalls++; Complete(); return ValueTask.CompletedTask; }
    }
}
