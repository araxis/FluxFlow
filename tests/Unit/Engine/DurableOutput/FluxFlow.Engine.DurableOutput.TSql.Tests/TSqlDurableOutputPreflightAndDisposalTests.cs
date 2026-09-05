using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.TSql.Tests;

public sealed class TSqlDurableOutputPreflightAndDisposalTests
{
    [Fact]
    public void Envelope_preflight_accepts_every_exact_column_length_boundary()
    {
        var address = ApplicationAddress.WorkflowPort(new string('w', 296), "C", "P");
        var envelope = TSqlDurableOutputTestData.Envelope(
            messageId: new string('m', 128),
            address: address,
            contractName: new string('c', 1024),
            traceId: new string('t', 512),
            correlationId: new string('r', 512),
            causationId: new string('a', 512),
            error: TSqlDurableOutputTestData.Error(
                code: new string('e', 1024),
                category: new string('g', 1024)));

        address.Value.Length.ShouldBe(300);
        Should.NotThrow(() => RelationalDurableOutputRows.ValidateEnvelope(envelope));
        Should.NotThrow(() => RelationalDurableOutputRows.ValidateLeaseOwner(new string('o', 512)));
    }

    [Theory]
    [InlineData(OverlengthField.Address, "application address", 300, "key")]
    [InlineData(OverlengthField.MessageId, "message identifier", 128, "key")]
    [InlineData(OverlengthField.ContractName, "contract name", 1024, "envelope")]
    [InlineData(OverlengthField.TraceId, "trace identifier", 512, "envelope")]
    [InlineData(OverlengthField.CorrelationId, "correlation identifier", 512, "envelope")]
    [InlineData(OverlengthField.CausationId, "causation identifier", 512, "envelope")]
    [InlineData(OverlengthField.ErrorCode, "error code", 1024, "envelope")]
    [InlineData(OverlengthField.ErrorCategory, "error category", 1024, "envelope")]
    public async Task Enqueue_rejects_each_overlength_field_before_database_io(
        OverlengthField field,
        string expectedField,
        int maximum,
        string expectedParameter)
    {
        await using var store = CreateStore();
        var envelope = CreateOverlengthEnvelope(field, maximum + 1);

        var exception = await Should.ThrowAsync<ArgumentException>(() =>
            store.EnqueueAsync(envelope).AsTask());

        exception.ParamName.ShouldBe(expectedParameter);
        exception.Message.ShouldContain(expectedField);
        exception.Message.ShouldContain(maximum.ToString());
        exception.ShouldNotBeOfType<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task Lease_rejects_overlength_owner_before_database_io()
    {
        await using var store = CreateStore();
        var request = new DurableOutputDeliveryLeaseRequest(
            new string('o', 513),
            TSqlDurableOutputTestData.Now,
            TSqlDurableOutputTestData.Now.AddMinutes(1));

        var exception = await Should.ThrowAsync<ArgumentException>(() =>
            store.TryLeaseAsync(request).AsTask());

        exception.Message.ShouldContain("lease owner");
        exception.Message.ShouldContain("512");
        exception.ShouldNotBeOfType<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task Precancelled_status_does_not_attempt_a_connection()
    {
        await using var store = CreateStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            store.GetStatusAsync(new(TSqlDurableOutputTestData.Now), cancellation.Token).AsTask());

        exception.CancellationToken.ShouldBe(cancellation.Token);
        exception.ShouldNotBeOfType<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task Null_status_query_is_rejected_before_connection_open()
    {
        await using var store = CreateStore();

        var exception = await Should.ThrowAsync<ArgumentNullException>(() =>
            store.GetStatusAsync(null!).AsTask());

        exception.ParamName.ShouldBe("query");
        exception.ShouldNotBeOfType<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task Renewal_null_and_precancellation_stop_before_database_io()
    {
        await using var store = CreateStore();
        var envelope = TSqlDurableOutputTestData.Envelope("renew-preflight");
        var renewal = new DurableOutputDeliveryLeaseRenewal(
            envelope.Key,
            Guid.Parse("ca59e7dc-511b-49dd-82f9-e0b63e476538"),
            TSqlDurableOutputTestData.Now,
            TSqlDurableOutputTestData.Now.AddMinutes(1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var nullException = await Should.ThrowAsync<ArgumentNullException>(() =>
            store.RenewLeaseAsync(null!).AsTask());
        var canceledException = await Should.ThrowAsync<OperationCanceledException>(() =>
            store.RenewLeaseAsync(renewal, cancellation.Token).AsTask());

        nullException.ParamName.ShouldBe("renewal");
        nullException.ShouldNotBeOfType<Microsoft.Data.SqlClient.SqlException>();
        canceledException.CancellationToken.ShouldBe(cancellation.Token);
        canceledException.ShouldNotBeOfType<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task Dispose_is_idempotent_and_every_operation_fails_predictably_without_io()
    {
        var store = CreateStore();
        var envelope = TSqlDurableOutputTestData.Envelope("disposed");
        var key = envelope.Key;
        var token = Guid.Parse("1e25bd0d-91b0-48db-a20e-186b8dc744c5");
        var leaseRequest = new DurableOutputDeliveryLeaseRequest(
            "worker",
            TSqlDurableOutputTestData.Now,
            TSqlDurableOutputTestData.Now.AddMinutes(1));

        await store.DisposeAsync();
        await store.DisposeAsync();

        await AssertDisposedAsync(() => store.EnqueueAsync(envelope).AsTask());
        await AssertDisposedAsync(() => store.TryLeaseAsync(leaseRequest).AsTask());
        await AssertDisposedAsync(() => store.RenewLeaseAsync(new(
            key,
            token,
            TSqlDurableOutputTestData.Now,
            TSqlDurableOutputTestData.Now.AddMinutes(1))).AsTask());
        await AssertDisposedAsync(() => store.CompleteAsync(new(
            key,
            token,
            TSqlDurableOutputTestData.Now)).AsTask());
        await AssertDisposedAsync(() => store.RetryAsync(new(
            key,
            token,
            TSqlDurableOutputTestData.Now,
            TSqlDurableOutputTestData.Now.AddMinutes(1))).AsTask());
        await AssertDisposedAsync(() => store.DeadLetterAsync(new(
            key,
            token,
            TSqlDurableOutputTestData.Now,
            DurableOutputDeadLetterReason.HandlerFailure)).AsTask());
        await AssertDisposedAsync(() => store.ListAsync(new()).AsTask());
        await AssertDisposedAsync(() => store.GetAsync(key).AsTask());
        await AssertDisposedAsync(() => store.ReplayAsync(new(
            key,
            expectedGeneration: 1,
            TSqlDurableOutputTestData.Now,
            TSqlDurableOutputTestData.Now.AddMinutes(1))).AsTask());
        await AssertDisposedAsync(() => store.GetStatusAsync(new(
            TSqlDurableOutputTestData.Now)).AsTask());
    }

    private static TSqlDurableOutputStore CreateStore()
        => new(new TSqlDurableOutputStoreOptions
        {
            ConnectionString = TSqlDurableOutputTestData.UnreachableConnectionString
        });

    private static async Task AssertDisposedAsync(Func<Task> operation)
    {
        var exception = await Should.ThrowAsync<ObjectDisposedException>(operation);
        exception.ObjectName.ShouldBe(typeof(TSqlDurableOutputStore).FullName);
    }

    private static DurableOutputEnvelope CreateOverlengthEnvelope(
        OverlengthField field,
        int length)
    {
        var value = new string('x', length);
        return TSqlDurableOutputTestData.Envelope(
            messageId: field == OverlengthField.MessageId ? value : "message",
            address: field == OverlengthField.Address
                ? ApplicationAddress.WorkflowPort(new string('x', length - 4), "C", "P")
                : null,
            contractName: field == OverlengthField.ContractName ? value : "contract",
            traceId: field == OverlengthField.TraceId ? value : "trace",
            correlationId: field == OverlengthField.CorrelationId ? value : "correlation",
            causationId: field == OverlengthField.CausationId ? value : "cause",
            error: field is OverlengthField.ErrorCode or OverlengthField.ErrorCategory
                ? TSqlDurableOutputTestData.Error(
                    code: field == OverlengthField.ErrorCode ? value : "error.code",
                    category: field == OverlengthField.ErrorCategory ? value : "validation")
                : null);
    }

    public enum OverlengthField
    {
        Address,
        MessageId,
        ContractName,
        TraceId,
        CorrelationId,
        CausationId,
        ErrorCode,
        ErrorCategory
    }
}
