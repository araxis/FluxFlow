using Microsoft.Extensions.DependencyInjection;
using FluxFlow.Json.Model;
using FluxFlow.Composition.Definition;
using System.Text.Json;
using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Resilience.Composition.Tests;

public sealed class ResilienceServiceCollectionExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "node", ResilienceComponentDefinition.Ports.Input);
    private static readonly ApplicationAddress Ack =
        ApplicationAddress.WorkflowPort("main", "node", ResilienceComponentDefinition.Ports.Ack);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", ResilienceComponentDefinition.Ports.Output);

    [Fact]
    public async Task Registration_declares_canonical_message_ports()
    {
        var services = new ServiceCollection().AddResilience();

        await using var provider = services.BuildServiceProvider();
        var type = ResilienceComponentDefinition.Types.Retry;
        var instance = await FluxFlowRegistrationExtensions.CreateFactoryMap(provider)[type](
            new ComponentActivationContext(provider, "main", "node", new ComponentDefinition(type, [])));
        try
        {
            instance.Inputs.Keys.ShouldBe(new[] { "Input", "Ack", "Nak", "Cancel" });
            instance.Outputs.Keys.ShouldBe(new[] { "Output", "Events" });
            instance.InputMetadata.Values.ShouldAllBe(p => p.MessageType == typeof(FlowValue) && p.Kind == ComponentPortKind.Message);
            instance.OutputMetadata.Values.ShouldAllBe(p => p.MessageType == typeof(FlowValue));
        }
        finally { await instance.DisposeAsync(); }
    }

    [Fact]
    public async Task Hosted_retry_accepts_ack_through_canonical_input()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                ResilienceComponentDefinition.Types.Retry,
                Properties(
                    ("strategy", "Fixed"),
                    ("initialDelayMilliseconds", 0),
                    ("maximumAttempts", 2),
                    ("attemptTimeoutMilliseconds", 10_000)), discardUnmatched: ["Output", "Events"]),
            registry => registry.AddResilience());
        host.StartResult.Succeeded.ShouldBeTrue(string.Join(
            Environment.NewLine,
            host.StartResult.Update?.Diagnostics.Select(failure =>
                $"{failure.Stage}: {failure.Error.Message} {failure.Error.Details}") ?? []));
        var ports = host.GetRequiredPorts();
        var message = FlowMessage.Create(JsonSerializer.SerializeToElement("payload"));

        var attemptReceive = ports.ReceiveAsync<RetrySignal<JsonElement>>(Output, Timeout);
        (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();
        var attempt = (await attemptReceive).Message.ShouldNotBeNull();
        attempt.Value.Status.ShouldBe(RetrySignalStatus.Attempt);

        var completedReceive = ports.ReceiveAsync<RetrySignal<JsonElement>>(Output, Timeout);
        (await ports.SendAsync(Ack, attempt.With("ack"))).IsAccepted.ShouldBeTrue();
        var completed = (await completedReceive).Message.ShouldNotBeNull();
        completed.Value.Status.ShouldBe(RetrySignalStatus.Completed);
        completed.TraceId.ShouldBe(message.TraceId);
    }

    [Fact]
    public async Task Invalid_configuration_is_rejected_during_preparation()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                ResilienceComponentDefinition.Types.Retry,
                Properties(("capacity", 0))),
            registry => registry.AddResilience());

        host.StartResult.Succeeded.ShouldBeFalse();
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hosted_retry_timer_failure_finishes_component_cleanup(bool disposalFails)
    {
        var clock = new CleanupClock { DisposalFails = disposalFails };
        var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(ResilienceComponentDefinition.Types.Retry,
                Properties(("Clock", "Resources.clock"), ("capacity", 4)), ["clock"], discardUnmatched: ["Output", "Events"]),
            registry => registry.AddResilience(),
            registerResources: context => context.Services.AddExternalFluxFlowResource<TimeProvider>(
                ApplicationAddress.Resource("clock"), clock));
        host.StartResult.Succeeded.ShouldBeTrue();
        var ports = host.GetRequiredPorts();
        foreach (var value in new[] { "first", "second" })
        {
            var message = FlowMessage.Create(JsonSerializer.SerializeToElement(value));
            var receive = ports.ReceiveAsync<RetrySignal<JsonElement>>(Output, Timeout);
            (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();
            var attempt = (await receive).Message.ShouldNotBeNull();
            attempt.TraceId.ShouldBe(message.TraceId);
            attempt.Value.Status.ShouldBe(RetrySignalStatus.Attempt);
            attempt.Value.Value.GetString().ShouldBe(value);
        }
        clock.StopFails = true;
        var error = await Should.ThrowAsync<AggregateException>(
            () => host.Application.StopAsync().AsTask().WaitAsync(Timeout));
        var failures = error.Flatten().InnerExceptions;
        failures.ShouldContain(item => ReferenceEquals(item, clock.StopFailure));
        if (disposalFails) failures.ShouldContain(item => ReferenceEquals(item, clock.DisposalFailure));
        failures.ShouldAllBe(item => ReferenceEquals(item, clock.StopFailure) ||
            disposalFails && ReferenceEquals(item, clock.DisposalFailure));
        host.Application.Current.ShouldBeNull();
        host.RuntimeAccess.Ports.ShouldBeNull();
        await host.Application.Events.Completion.WaitAsync(Timeout);
        await host.DisposeAsync();
        await host.DisposeAsync();
        clock.TimerDisposeCalls.ShouldBe(1);
        clock.DisposeCalls.ShouldBe(0);
    }

    private sealed class CleanupClock : TimeProvider, IDisposable
    {
        private readonly FakeTimeProvider _inner = new();
        public bool StopFails { get; set; }
        public bool DisposalFails { get; set; }
        public InvalidOperationException StopFailure { get; } = new("hosted retry timer stop failed");
        public InvalidOperationException DisposalFailure { get; } = new("hosted retry timer disposal failed");
        public int TimerDisposeCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public void Dispose() => DisposeCalls++;
        public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new Timer(_inner.CreateTimer(callback, state, dueTime, period), this);
        private sealed class Timer(ITimer inner, CleanupClock owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
                => owner.StopFails && dueTime == global::System.Threading.Timeout.InfiniteTimeSpan
                    ? throw owner.StopFailure : inner.Change(dueTime, period);
            public void Dispose() => throw new NotSupportedException();
            public async ValueTask DisposeAsync()
            {
                owner.TimerDisposeCalls++;
                await inner.DisposeAsync();
                if (owner.DisposalFails) throw owner.DisposalFailure;
            }
        }
    }

}
