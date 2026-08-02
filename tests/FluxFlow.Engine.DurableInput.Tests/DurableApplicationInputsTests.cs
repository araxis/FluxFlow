using FluxFlow.Engine.DurableInput;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

public sealed class DurableApplicationInputsTests
{
    [Fact]
    public async Task Enqueue_persists_the_exact_message_identity_contract_and_clock_time()
    {
        var clock = new FakeTimeProvider(DurableInputTestData.Now);
        var store = new DurableInputTestStore();
        var client = CreateClient(store, clock);
        var message = FlowMessage.Restore(
            "payload",
            new MessageId("message-42"),
            new TraceId("trace-42"),
            DurableInputTestData.Now.AddMinutes(-2),
            new CorrelationId("order-42"),
            new MessageId("cause-41"),
            new Dictionary<string, string> { ["tenant"] = "north" });

        var result = await client.EnqueueAsync(DurableInputTestData.Input, message);
        var stored = store.Get(result.Key);

        result.Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        result.IsAccepted.ShouldBeTrue();
        result.Key.ShouldBe(new DurableInputKey(DurableInputTestData.Input, message.MessageId));
        stored.State.ShouldBe(DurableInputState.Pending);
        stored.Envelope.ContractName.ShouldBe("text-v1");
        stored.Envelope.Payload.GetString().ShouldBe("payload");
        stored.Envelope.MessageId.ShouldBe(message.MessageId);
        stored.Envelope.TraceId.ShouldBe(message.TraceId);
        stored.Envelope.Timestamp.ShouldBe(message.Timestamp);
        stored.Envelope.CorrelationId.ShouldBe(message.CorrelationId);
        stored.Envelope.CausationId.ShouldBe(message.CausationId);
        stored.Envelope.Headers.ShouldBe(message.Headers);
        stored.Envelope.EnqueuedAt.ShouldBe(DurableInputTestData.Now);
    }

    [Fact]
    public async Task Equal_duplicate_is_accepted_but_changed_content_conflicts_without_overwrite()
    {
        var clock = new FakeTimeProvider(DurableInputTestData.Now);
        var store = new DurableInputTestStore();
        var client = CreateClient(store, clock);
        var original = FlowMessage.Restore(
            "original",
            new MessageId("message-idempotent"),
            new TraceId("trace-idempotent"),
            DurableInputTestData.Now);

        var first = await client.EnqueueAsync(DurableInputTestData.Input, original);
        clock.Advance(TimeSpan.FromMinutes(1));
        var duplicate = await client.EnqueueAsync(DurableInputTestData.Input, original);
        var changed = await client.EnqueueAsync(
            DurableInputTestData.Input,
            FlowMessage.Restore(
                "changed",
                original.MessageId,
                original.TraceId,
                original.Timestamp));

        first.Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        duplicate.Status.ShouldBe(DurableInputEnqueueStatus.AlreadyExists);
        duplicate.IsAccepted.ShouldBeTrue();
        changed.Status.ShouldBe(DurableInputEnqueueStatus.Conflict);
        changed.IsAccepted.ShouldBeFalse();
        store.Get(first.Key).Envelope.Payload.GetString().ShouldBe("original");
        store.Get(first.Key).Envelope.EnqueuedAt.ShouldBe(DurableInputTestData.Now);
    }

    [Fact]
    public async Task Same_message_id_is_independent_at_a_second_application_address()
    {
        var clock = new FakeTimeProvider(DurableInputTestData.Now);
        var store = new DurableInputTestStore();
        var client = CreateClient(store, clock);
        var message = FlowMessage.Restore(
            "payload",
            new MessageId("message-shared"),
            new TraceId("trace-shared"),
            DurableInputTestData.Now);
        var secondInput = FluxFlow.Composition.Addressing.ApplicationAddress.WorkflowPort(
            "Orders",
            "Alternate",
            "Input");

        var first = await client.EnqueueAsync(DurableInputTestData.Input, message);
        var second = await client.EnqueueAsync(secondInput, message);

        first.Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        second.Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        first.Key.MessageId.ShouldBe(second.Key.MessageId);
        first.Key.Address.ShouldBe(DurableInputTestData.Input);
        second.Key.Address.ShouldBe(secondInput);
        first.Key.ShouldNotBe(second.Key);
        store.Get(first.Key).Envelope.Address.ShouldBe(DurableInputTestData.Input);
        store.Get(second.Key).Envelope.Address.ShouldBe(secondInput);
    }

    [Fact]
    public async Task Store_failure_is_propagated_without_a_direct_engine_fallback()
    {
        var expected = new IOException("store unavailable");
        var store = new DurableInputTestStore { EnqueueException = expected };
        var client = CreateClient(store, new FakeTimeProvider(DurableInputTestData.Now));

        var thrown = await Should.ThrowAsync<IOException>(async () =>
            await client.EnqueueAsync(
                DurableInputTestData.Input,
                FlowMessage.Create("payload")));

        thrown.ShouldBeSameAs(expected);
        store.EnqueueCalls.ShouldBe(1);
        store.LeaseRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Enqueue_rejects_non_port_application_addresses_before_store_access()
    {
        var store = new DurableInputTestStore();
        var client = CreateClient(store, new FakeTimeProvider(DurableInputTestData.Now));

        var exception = await Should.ThrowAsync<ArgumentException>(async () =>
            await client.EnqueueAsync(
                FluxFlow.Composition.Addressing.ApplicationAddress.Resource("database"),
                FlowMessage.Create("payload")));

        exception.ParamName.ShouldBe("input");
        store.EnqueueCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Unregistered_contract_fails_before_store_access_and_cancellation_reaches_store()
    {
        var clock = new FakeTimeProvider(DurableInputTestData.Now);
        var store = new DurableInputTestStore();
        var client = CreateClient(store, clock);

        var missing = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await client.EnqueueAsync(DurableInputTestData.Input, FlowMessage.Create(42)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await client.EnqueueAsync(
                DurableInputTestData.Input,
                FlowMessage.Create("payload"),
                cancellation.Token));

        missing.Message.ShouldContain(typeof(int).ToString());
    }

    private static DurableApplicationInputs CreateClient(
        IDurableInputStore store,
        TimeProvider clock)
        => new(
            store,
            new DurableInputContractRegistry(
            [
                new DurableInputContract<string>("text-v1", jsonTypeInfo: null)
            ]),
            clock);
}
