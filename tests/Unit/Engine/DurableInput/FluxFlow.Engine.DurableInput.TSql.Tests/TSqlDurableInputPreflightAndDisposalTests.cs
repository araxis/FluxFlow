using System.Text.Json;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.TSql.Tests;

public sealed class TSqlDurableInputPreflightAndDisposalTests
{
    [Fact]
    public void Envelope_and_lease_preflight_accept_every_exact_column_length_boundary()
    {
        var address = ApplicationAddress.WorkflowPort(new string('w', 296), "C", "P");
        var envelope = TSqlDurableInputTestData.Envelope(
            messageId: new string('m', 128),
            address: address,
            contractName: new string('c', 1024),
            traceId: new string('t', 512),
            correlationId: new string('r', 512),
            causationId: new string('a', 512),
            error: TSqlDurableInputTestData.Error(
                code: new string('e', 1024),
                category: new string('g', 1024)));

        address.Value.Length.ShouldBe(300);
        Should.NotThrow(() => RelationalDurableInputRows.ValidateEnvelope(envelope));
        Should.NotThrow(() => RelationalDurableInputRows.ValidateLeaseRequest(new(
            new string('o', 512),
            TSqlDurableInputTestData.Now,
            TSqlDurableInputTestData.Now.AddMinutes(1),
            1)));
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
    public async Task Enqueue_rejects_each_overlength_identity_field_before_database_io(
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
        exception.ShouldNotBeOfType<SqlException>();
    }

    [Fact]
    public async Task Lease_rejects_overlength_owner_before_database_io()
    {
        await using var store = CreateStore();
        var request = new DurableInputLeaseRequest(
            new string('o', 513),
            TSqlDurableInputTestData.Now,
            TSqlDurableInputTestData.Now.AddMinutes(1),
            1);

        var exception = await Should.ThrowAsync<ArgumentException>(() =>
            store.LeaseAsync(request).AsTask());

        exception.Message.ShouldContain("lease owner");
        exception.Message.ShouldContain("512");
        exception.ShouldNotBeOfType<SqlException>();
    }

    [Fact]
    public void Unbounded_payload_headers_error_details_and_failure_description_have_no_preflight_limit()
    {
        var large = new string('x', 70_000);
        var valueEnvelope = new DurableInputEnvelope(
            ApplicationAddress.WorkflowPort("Orders", "Receiver", "Input"),
            "contract",
            isError: false,
            JsonSerializer.SerializeToElement(new { value = large }),
            error: null,
            new MessageId("large"),
            new TraceId("trace"),
            TSqlDurableInputTestData.Now,
            TSqlDurableInputTestData.Now,
            null,
            null,
            new Dictionary<string, string> { [large] = large },
            DurableInputEnvelope.CurrentSchemaVersion);
        var errorEnvelope = TSqlDurableInputTestData.Envelope(
            messageId: "large-error",
            error: new FlowError("code", large, "category", false,
                JsonSerializer.SerializeToElement(new { value = large })));

        Should.NotThrow(() => RelationalDurableInputRows.ValidateEnvelope(valueEnvelope));
        Should.NotThrow(() => RelationalDurableInputRows.ValidateEnvelope(errorEnvelope));
        Should.NotThrow(() => new DurableInputFailure(DurableInputFailureKind.InvalidEnvelope, large));
    }

    [Fact]
    public async Task Precancelled_operation_does_not_initialize_schema_or_attempt_connection()
    {
        await using var store = CreateStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            store.EnqueueAsync(TSqlDurableInputTestData.Envelope(), cancellation.Token).AsTask());

        exception.CancellationToken.ShouldBe(cancellation.Token);
        exception.ShouldNotBeOfType<SqlException>();
    }

    [Fact]
    public async Task Precancelled_status_does_not_attempt_a_connection()
    {
        await using var store = CreateStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            store.GetStatusAsync(new(TSqlDurableInputTestData.Now), cancellation.Token).AsTask());

        exception.CancellationToken.ShouldBe(cancellation.Token);
        exception.ShouldNotBeOfType<SqlException>();
    }

    [Fact]
    public async Task Null_status_query_is_rejected_before_connection_open()
    {
        await using var store = CreateStore();

        var exception = await Should.ThrowAsync<ArgumentNullException>(() =>
            store.GetStatusAsync(null!).AsTask());

        exception.ParamName.ShouldBe("query");
        exception.ShouldNotBeOfType<SqlException>();
    }

    [Fact]
    public async Task Dispose_is_idempotent_and_every_operation_fails_predictably_without_io()
    {
        var store = CreateStore();
        var envelope = TSqlDurableInputTestData.Envelope("disposed");
        var key = envelope.Key;
        var token = Guid.Parse("1e25bd0d-91b0-48db-a20e-186b8dc744c5");
        var now = TSqlDurableInputTestData.Now;
        var failure = new DurableInputFailure(DurableInputFailureKind.InvalidEnvelope, "invalid");

        await store.DisposeAsync();
        await store.DisposeAsync();

        await AssertDisposedAsync(() => store.EnqueueAsync(envelope).AsTask());
        await AssertDisposedAsync(() => store.LeaseAsync(new("worker", now, now.AddMinutes(1), 1)).AsTask());
        await AssertDisposedAsync(() => store.MarkDeliveredAsync(new(key, token, now)).AsTask());
        await AssertDisposedAsync(() => store.RenewLeaseAsync(new(key, token, now, now.AddMinutes(1))).AsTask());
        await AssertDisposedAsync(() => store.ReleaseAsync(new(key, token, now, now, failure)).AsTask());
        await AssertDisposedAsync(() => store.DeadLetterAsync(new(key, token, now, failure)).AsTask());
        await AssertDisposedAsync(() => store.ListAsync(new()).AsTask());
        await AssertDisposedAsync(() => store.GetAsync(key).AsTask());
        await AssertDisposedAsync(() => store.ReplayAsync(new(key, 1, now, now)).AsTask());
        await AssertDisposedAsync(() => store.GetStatusAsync(new(now)).AsTask());
    }

    private static TSqlDurableInputStore CreateStore()
        => new(new TSqlDurableInputStoreOptions
        {
            ConnectionString = TSqlDurableInputTestData.UnreachableConnectionString
        });

    private static async Task AssertDisposedAsync(Func<Task> operation)
    {
        var exception = await Should.ThrowAsync<ObjectDisposedException>(operation);
        exception.ObjectName.ShouldBe(typeof(TSqlDurableInputStore).FullName);
    }

    private static DurableInputEnvelope CreateOverlengthEnvelope(OverlengthField field, int length)
    {
        var value = new string('x', length);
        return TSqlDurableInputTestData.Envelope(
            messageId: field == OverlengthField.MessageId ? value : "message",
            address: field == OverlengthField.Address
                ? ApplicationAddress.WorkflowPort(new string('x', length - 4), "C", "P")
                : null,
            contractName: field == OverlengthField.ContractName ? value : "contract",
            traceId: field == OverlengthField.TraceId ? value : "trace",
            correlationId: field == OverlengthField.CorrelationId ? value : "correlation",
            causationId: field == OverlengthField.CausationId ? value : "cause",
            error: field is OverlengthField.ErrorCode or OverlengthField.ErrorCategory
                ? TSqlDurableInputTestData.Error(
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
