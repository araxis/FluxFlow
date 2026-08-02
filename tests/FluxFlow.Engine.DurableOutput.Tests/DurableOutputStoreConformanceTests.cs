using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

/// <summary>
/// Executable provider-neutral conformance specification for
/// <see cref="IDurableOutputStore"/> implementations.
/// </summary>
public abstract class DurableOutputStoreConformanceTests
{
    public static TheoryData<DurableOutputContentMutation> ConflictMutations =>
    [
        DurableOutputContentMutation.ContractName,
        DurableOutputContentMutation.ValueOrErrorCase,
        DurableOutputContentMutation.Payload,
        DurableOutputContentMutation.TraceId,
        DurableOutputContentMutation.Timestamp,
        DurableOutputContentMutation.CorrelationId,
        DurableOutputContentMutation.CausationId,
        DurableOutputContentMutation.Headers,
        DurableOutputContentMutation.SchemaVersion
    ];

    protected abstract ValueTask<DurableOutputStoreTestContext> CreateStoreAsync();

    [Fact]
    public async Task Enqueue_is_idempotent_by_address_and_message_id_without_overwrite()
    {
        await using var context = await CreateStoreAsync();
        var original = DurableOutputStoreConformanceData.Envelope();
        var recaptured = DurableOutputStoreConformanceData.Copy(
            original,
            capturedAt: original.CapturedAt.AddMinutes(1));

        var first = await context.Store.EnqueueAsync(original);
        var duplicate = await context.Store.EnqueueAsync(recaptured);
        var retained = await context.ReadAsync(original.Key);

        first.ShouldBe(new DurableOutputEnqueueResult(
            original.Key,
            DurableOutputEnqueueStatus.Enqueued));
        duplicate.ShouldBe(new DurableOutputEnqueueResult(
            original.Key,
            DurableOutputEnqueueStatus.AlreadyExists));
        retained.ShouldNotBeNull().HasSameContent(original).ShouldBeTrue();
        retained.CapturedAt.ShouldBe(original.CapturedAt);
        retained.Payload.GetRawText().ShouldBe(original.Payload.GetRawText());
    }

    [Fact]
    public async Task Enqueue_scopes_the_same_message_id_to_each_output_address()
    {
        await using var context = await CreateStoreAsync();
        var primary = DurableOutputStoreConformanceData.Envelope();
        var secondary = DurableOutputStoreConformanceData.Envelope(
            address: DurableOutputStoreConformanceData.SecondaryOutput);

        var primaryResult = await context.Store.EnqueueAsync(primary);
        var secondaryResult = await context.Store.EnqueueAsync(secondary);
        var retainedPrimary = await context.ReadAsync(primary.Key);
        var retainedSecondary = await context.ReadAsync(secondary.Key);

        primaryResult.Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        secondaryResult.Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        retainedPrimary.ShouldNotBeNull().HasSameContent(primary).ShouldBeTrue();
        retainedSecondary.ShouldNotBeNull().HasSameContent(secondary).ShouldBeTrue();
        retainedPrimary.Key.ShouldNotBe(retainedSecondary.Key);
    }

    [Theory]
    [MemberData(nameof(ConflictMutations))]
    public async Task Meaningful_content_change_conflicts_without_overwriting_winner(
        DurableOutputContentMutation mutation)
    {
        await using var context = await CreateStoreAsync();
        var original = DurableOutputStoreConformanceData.Envelope();
        var changed = DurableOutputStoreConformanceData.MutateSameKey(original, mutation);

        var first = await context.Store.EnqueueAsync(original);
        var conflict = await context.Store.EnqueueAsync(changed);
        var retained = await context.ReadAsync(original.Key);

        first.Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        conflict.ShouldBe(new DurableOutputEnqueueResult(
            original.Key,
            DurableOutputEnqueueStatus.Conflict));
        retained.ShouldNotBeNull().HasSameContent(original).ShouldBeTrue();
        retained.HasSameContent(changed).ShouldBeFalse();
        retained.CapturedAt.ShouldBe(original.CapturedAt);
    }

    [Fact]
    public async Task Precancelled_enqueue_creates_no_record_and_cannot_overwrite_a_winner()
    {
        await using var context = await CreateStoreAsync();
        var original = DurableOutputStoreConformanceData.Envelope();
        var changed = DurableOutputStoreConformanceData.MutateSameKey(
            original,
            DurableOutputContentMutation.Payload);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.Store.EnqueueAsync(original, canceled.Token).AsTask());
        (await context.ReadAsync(original.Key)).ShouldBeNull();
        (await context.Store.EnqueueAsync(original))
            .Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        await Should.ThrowAsync<OperationCanceledException>(() =>
            context.Store.EnqueueAsync(changed, canceled.Token).AsTask());

        var retained = await context.ReadAsync(original.Key);
        retained.ShouldNotBeNull().HasSameContent(original).ShouldBeTrue();
        retained.HasSameContent(changed).ShouldBeFalse();
    }

    [Fact]
    public async Task Enqueue_rejects_null_envelope_without_observer_mutation()
    {
        await using var context = await CreateStoreAsync();
        var exception = await Should.ThrowAsync<ArgumentNullException>(() =>
            context.Store.EnqueueAsync(null!).AsTask());

        exception.ParamName.ShouldBe("envelope");
        (await context.ReadAsync(DurableOutputStoreConformanceData.Envelope().Key))
            .ShouldBeNull();
    }
}
