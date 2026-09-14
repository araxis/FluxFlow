using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class SourceShutdownTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("complete")]
    [InlineData("fault")]
    [InlineData("dispose")]
    public async Task Source_shutdown_before_start_prevents_later_production(string operation)
    {
        var source = new SourceProbe(static (_, _) => Task.CompletedTask);
        var failure = new InvalidOperationException("not started");
        if (operation == "fault") source.Fault(failure);
        else if (operation == "dispose") await source.DisposeAsync();
        else source.Complete();

        if (operation == "fault")
            (await Should.ThrowAsync<InvalidOperationException>(() => source.Completion.WaitAsync(Timeout)))
                .ShouldBeSameAs(failure);
        else await source.Completion.WaitAsync(Timeout);

        await source.StartAsync();
        await source.StartAsync();
        await source.DisposeAsync().AsTask().WaitAsync(Timeout);
        source.RunCount.ShouldBe(0);
        source.DisposeCount.ShouldBe(1);
        source.Output.Completion.IsCompleted.ShouldBeTrue();
        source.Extra.Completion.IsCompleted.ShouldBeTrue();
        await source.Events.Completion.WaitAsync(Timeout);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("fault")]
    [InlineData("dispose")]
    public async Task Source_shutdown_joins_work_despite_callback_failure(string operation)
    {
        var entered = Gate();
        var cancelled = Gate();
        var release = Gate();
        var failure = new InvalidOperationException("source failed");
        var callbackFailure = new InvalidOperationException("callback failed");
        var source = new SourceProbe(async (_, token) =>
        {
            using var registration = token.Register(() =>
            {
                cancelled.TrySetResult();
                throw callbackFailure;
            });
            entered.TrySetResult();
            await release.Task;
        });
        Task? disposal = null;
        try
        {
            await source.StartAsync();
            await entered.Task.WaitAsync(Timeout);
            if (operation == "fault") source.Fault(failure);
            else if (operation == "dispose") disposal = source.DisposeAsync().AsTask();
            else source.Complete();
            await cancelled.Task.WaitAsync(Timeout);
            disposal ??= source.DisposeAsync().AsTask();
            disposal.IsCompleted.ShouldBeFalse();
            source.Completion.IsCompleted.ShouldBeFalse();
            source.DisposeCount.ShouldBe(0);
        }
        finally { release.TrySetResult(); }

        var actual = await Should.ThrowAsync<Exception>(() => source.Completion.WaitAsync(Timeout));
        var errors = Flatten(actual);
        errors.ShouldBe(operation == "fault" ? [failure, callbackFailure] : [callbackFailure]);
        await disposal!.WaitAsync(Timeout);
        source.RunCount.ShouldBe(1);
        source.DisposeCount.ShouldBe(1);
        source.Exited.ShouldBeTrue();
        source.Output.Completion.IsFaulted.ShouldBeTrue();
        source.Extra.Completion.IsFaulted.ShouldBeTrue();
        await source.Events.Completion.WaitAsync(Timeout);
        source.Complete();
        source.Fault(new InvalidOperationException("late fault"));
        await source.StartAsync();
        await source.DisposeAsync();
        source.RunCount.ShouldBe(1);
        source.DisposeCount.ShouldBe(1);
        Flatten(await Should.ThrowAsync<Exception>(() => source.Completion)).ShouldBe(errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_output_failure_joins_production_despite_callback_failure(bool extra)
    {
        var entered = Gate();
        var cancelled = Gate();
        var release = Gate();
        var failure = new InvalidOperationException("output failed");
        var callbackFailure = new InvalidOperationException("output cancellation failed");
        var source = new SourceProbe(async (_, token) =>
        {
            using var registration = token.Register(() =>
            {
                cancelled.TrySetResult();
                throw callbackFailure;
            });
            entered.TrySetResult();
            await release.Task;
        });
        Task? disposal = null;
        try
        {
            await source.StartAsync();
            await entered.Task.WaitAsync(Timeout);
            (extra ? source.Extra : source.Output).Fault(failure);
            await cancelled.Task.WaitAsync(Timeout);
            disposal = source.DisposeAsync().AsTask();
            disposal.IsCompleted.ShouldBeFalse();
            source.Completion.IsCompleted.ShouldBeFalse();
            source.DisposeCount.ShouldBe(0);
        }
        finally { release.TrySetResult(); }

        var actual = await Should.ThrowAsync<AggregateException>(() => source.Completion.WaitAsync(Timeout));
        Flatten(actual).ShouldBe([failure, callbackFailure]);
        await disposal!.WaitAsync(Timeout);
        source.Exited.ShouldBeTrue();
        source.DisposeCount.ShouldBe(1);
        source.Output.Completion.IsFaulted.ShouldBeTrue();
        source.Extra.Completion.IsFaulted.ShouldBeTrue();
        await source.Events.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Source_premature_output_completion_stops_production_and_faults_siblings()
    {
        var entered = Gate();
        var cancelled = Gate();
        var release = Gate();
        var source = new SourceProbe(async (_, token) =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            entered.TrySetResult();
            await release.Task;
        });
        try
        {
            await source.StartAsync();
            await entered.Task.WaitAsync(Timeout);
            source.Extra.Complete();
            await cancelled.Task.WaitAsync(Timeout);
            source.Completion.IsCompleted.ShouldBeFalse();
            source.DisposeCount.ShouldBe(0);
        }
        finally { release.TrySetResult(); }
        var failure = await Should.ThrowAsync<InvalidOperationException>(() => source.Completion.WaitAsync(Timeout));
        failure.Message.ShouldBe("A source data output completed before source production stopped.");
        (await Should.ThrowAsync<InvalidOperationException>(() => source.Output.Completion.WaitAsync(Timeout)))
            .ShouldBeSameAs(failure);
        source.Extra.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        await source.DisposeAsync().AsTask().WaitAsync(Timeout);
        source.Exited.ShouldBeTrue();
        source.DisposeCount.ShouldBe(1);
        await source.Events.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Source_cancellation_callback_can_request_completion()
    {
        var entered = Gate();
        var cancelled = Gate();
        var release = Gate();
        var source = new SourceProbe(async (owner, token) =>
        {
            using var registration = token.Register(() =>
            {
                owner.Complete();
                cancelled.TrySetResult();
            });
            entered.TrySetResult();
            await release.Task;
        });
        try
        {
            await source.StartAsync();
            await entered.Task.WaitAsync(Timeout);
            source.Complete();
            await cancelled.Task.WaitAsync(Timeout);
            source.Completion.IsCompleted.ShouldBeFalse();
        }
        finally { release.TrySetResult(); }
        await source.Completion.WaitAsync(Timeout);
        await source.DisposeAsync().AsTask().WaitAsync(Timeout);
        source.RunCount.ShouldBe(1);
        source.DisposeCount.ShouldBe(1);
        source.Exited.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_completion_joins_cancellation_callbacks_before_releasing_resources(bool callbackFails)
    {
        var entered = Gate();
        var callbackEntered = Gate();
        var releaseProduction = Gate();
        var productionExited = Gate();
        using var releaseCallback = new ManualResetEventSlim();
        var failure = new InvalidOperationException("late cancellation failure");
        CancellationTokenRegistration registration = default;
        var source = new SourceProbe(async (_, token) =>
        {
            registration = token.Register(() =>
            {
                callbackEntered.TrySetResult();
                releaseCallback.Wait(Timeout).ShouldBeTrue();
                if (callbackFails) throw failure;
            });
            entered.TrySetResult();
            await releaseProduction.Task;
            productionExited.TrySetResult();
        });
        Task? disposal = null;
        try
        {
            await source.StartAsync();
            await entered.Task.WaitAsync(Timeout);
            var stop = Task.Run(source.Complete);
            await callbackEntered.Task.WaitAsync(Timeout);
            await stop.WaitAsync(Timeout);
            releaseProduction.TrySetResult();
            await productionExited.Task.WaitAsync(Timeout);
            disposal = source.DisposeAsync().AsTask();
            disposal.IsCompleted.ShouldBeFalse();
            source.Completion.IsCompleted.ShouldBeFalse();
            source.DisposeCount.ShouldBe(0);
        }
        finally
        {
            releaseProduction.TrySetResult();
            releaseCallback.Set();
        }
        if (callbackFails)
            (await Should.ThrowAsync<InvalidOperationException>(() => source.Completion.WaitAsync(Timeout)))
                .ShouldBeSameAs(failure);
        else await source.Completion.WaitAsync(Timeout);
        await disposal!.WaitAsync(Timeout);
        await registration.DisposeAsync();
        source.Exited.ShouldBeTrue();
        source.DisposeCount.ShouldBe(1);
        source.RunCount.ShouldBe(1);
        await source.Events.Completion.WaitAsync(Timeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_production_failure_reaches_all_outputs(bool unrequestedCancellation)
    {
        Exception failure = unrequestedCancellation
            ? new OperationCanceledException("not a requested stop")
            : new InvalidOperationException("producer failed");
        var source = new SourceProbe((_, _) => Task.FromException(failure));
        await source.StartAsync();
        foreach (var completion in new[] { source.Completion, source.Output.Completion, source.Extra.Completion })
        {
            (await Should.ThrowAsync<Exception>(() => completion.WaitAsync(Timeout))).ShouldBeSameAs(failure);
            completion.IsFaulted.ShouldBeTrue();
        }
        await source.DisposeAsync().AsTask().WaitAsync(Timeout);
        source.RunCount.ShouldBe(1);
        source.DisposeCount.ShouldBe(1);
        source.Exited.ShouldBeTrue();
        await source.Events.Completion.WaitAsync(Timeout);
    }

    [Fact]
    public async Task Source_production_fault_releases_outputs_before_callbacks_finish()
    {
        var callbackEntered = Gate();
        using var releaseCallback = new ManualResetEventSlim();
        CancellationTokenRegistration registration = default;
        var failure = new InvalidOperationException("release data routes immediately");
        var source = new SourceProbe((_, token) =>
        {
            registration = token.Register(() =>
            {
                callbackEntered.TrySetResult();
                releaseCallback.Wait(Timeout + Timeout).ShouldBeTrue();
            });
            return Task.FromException(failure);
        });
        try
        {
            await source.StartAsync();
            await callbackEntered.Task.WaitAsync(Timeout);
            (await Should.ThrowAsync<InvalidOperationException>(() => source.Output.Completion.WaitAsync(Timeout)))
                .ShouldBeSameAs(failure);
            (await Should.ThrowAsync<InvalidOperationException>(() => source.Extra.Completion.WaitAsync(Timeout)))
                .ShouldBeSameAs(failure);
            source.Completion.IsCompleted.ShouldBeFalse();
            source.DisposeCount.ShouldBe(0);
        }
        finally { releaseCallback.Set(); }
        (await Should.ThrowAsync<InvalidOperationException>(() => source.Completion.WaitAsync(Timeout)))
            .ShouldBeSameAs(failure);
        await source.DisposeAsync().AsTask().WaitAsync(Timeout);
        await registration.DisposeAsync();
        source.RunCount.ShouldBe(1);
        source.DisposeCount.ShouldBe(1);
        source.Exited.ShouldBeTrue();
        await source.Events.Completion.WaitAsync(Timeout);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_natural_completion_preserves_fanout_and_owned_cleanup(bool cleanupFails)
    {
        var messages = Enumerable.Range(1, 3).Select(value => FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))))).ToArray();
        var failure = new InvalidOperationException("source cleanup failed");
        var source = new SourceProbe(async (owner, token) =>
        {
            foreach (var message in messages)
            {
                await owner.EmitBothAsync(message, token);
            }
        }, cleanupFails ? failure : null);
        var first = Sink(source.Output);
        var second = Sink(source.Output);
        var extra = Sink(source.Extra);
        var stopping = source.Token;
        await source.StartAsync();
        await source.Completion.WaitAsync(Timeout);
        stopping.IsCancellationRequested.ShouldBeTrue();
        foreach (var message in messages)
        {
            (await first.ReceiveAsync().WaitAsync(Timeout)).ShouldBeSameAs(message);
            (await second.ReceiveAsync().WaitAsync(Timeout)).ShouldBeSameAs(message);
            (await extra.ReceiveAsync().WaitAsync(Timeout)).ShouldBeSameAs(message);
        }
        await Task.WhenAll(first.Completion, second.Completion, extra.Completion).WaitAsync(Timeout);
        if (cleanupFails)
            (await Should.ThrowAsync<InvalidOperationException>(() => source.DisposeAsync().AsTask().WaitAsync(Timeout)))
                .ShouldBeSameAs(failure);
        else await source.DisposeAsync().AsTask().WaitAsync(Timeout);
        await source.DisposeAsync();
        source.DisposeCount.ShouldBe(1);
        source.Exited.ShouldBeTrue();
        source.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        (await source.Extra.SendAsync(messages[0])).ShouldBeFalse();
        Should.Throw<ObjectDisposedException>(() => { _ = source.Token; });
    }

    private sealed class SourceProbe : FlowSource
    {
        private readonly Func<SourceProbe, CancellationToken, Task> _run;
        private readonly Exception? _cleanupFailure;
        public FlowOutput<FlowMessage> Extra { get; }
        public int RunCount { get; private set; }
        public int DisposeCount { get; private set; }
        public bool Exited { get; private set; }
        public CancellationToken Token => Stopping;

        public SourceProbe(Func<SourceProbe, CancellationToken, Task> run, Exception? cleanupFailure = null)
            : base(new FlowSourceOptions { OutputCapacity = 2 })
        {
            _run = run;
            _cleanupFailure = cleanupFailure;
            Extra = AddOutput<FlowMessage>();
        }

        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            RunCount++;
            try { await _run(this, cancellationToken); }
            finally { Exited = true; }
        }

        public async Task EmitBothAsync(FlowMessage message, CancellationToken token)
        {
            await EmitAsync(message, token);
            await EmitAsync(Extra, message, token);
        }

        protected override ValueTask OnDisposeAsync()
        {
            DisposeCount++;
            if (_cleanupFailure is not null) throw _cleanupFailure;
            return ValueTask.CompletedTask;
        }
    }

    private static IReadOnlyList<Exception> Flatten(Exception error)
        => error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [error];
    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static BufferBlock<FlowMessage> Sink(ISourceBlock<FlowMessage> source)
    {
        var sink = new BufferBlock<FlowMessage>();
        source.LinkTo(sink, new DataflowLinkOptions { PropagateCompletion = true });
        return sink;
    }
}
