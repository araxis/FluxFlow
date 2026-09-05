using System.Globalization;
using FluxFlow.Engine.DurableInput.Tests;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

public sealed class SqlFileDurableInputLeaseRenewalPersistenceTests
{
    [Fact]
    public async Task Renewal_changes_only_the_exact_persisted_lease_expiry()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore();
        var envelope = SqlFileDurableInputTestData.CompleteErrorEnvelope();
        await store.EnqueueAsync(envelope);
        var baseTime = envelope.EnqueuedAt.AddMinutes(1);
        var lease = (await store.LeaseAsync(DurableInputStoreConformanceData.Request(
            ownerId: "renewal-owner",
            now: baseTime,
            leaseUntil: baseTime.AddMinutes(2)))).Single();
        var before = await ReadRowAsync(database.DatabasePath, envelope.MessageId.Value);
        var requestedUntil = baseTime.AddSeconds(45);

        var result = await store.RenewLeaseAsync(new DurableInputLeaseRenewal(
            envelope.Key,
            lease.LeaseToken,
            baseTime.AddSeconds(1),
            requestedUntil));
        var after = await ReadRowAsync(database.DatabasePath, envelope.MessageId.Value);

        result.Status.ShouldBe(DurableInputTransitionStatus.Applied);
        before.Keys.ShouldBe(after.Keys, ignoreOrder: false);
        after["lease_until_utc_ticks"].ShouldBe(requestedUntil.UtcTicks.ToString(
            CultureInfo.InvariantCulture));
        before["lease_until_utc_ticks"].ShouldNotBe(after["lease_until_utc_ticks"]);
        after.Where(item => !string.Equals(
                before[item.Key],
                item.Value,
                StringComparison.Ordinal))
            .Select(item => item.Key)
            .ShouldBe(["lease_until_utc_ticks"]);
    }

    private static async ValueTask<IReadOnlyDictionary<string, string?>> ReadRowAsync(
        string databasePath,
        string messageId)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT *
            FROM fluxflow_durable_inputs
            WHERE message_id = $messageId;
            """;
        command.Parameters.AddWithValue("$messageId", messageId);
        await using var row = await command.ExecuteReaderAsync();
        (await row.ReadAsync()).ShouldBeTrue();
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var index = 0; index < row.FieldCount; index++)
        {
            values.Add(
                row.GetName(index),
                row.IsDBNull(index)
                    ? null
                    : Convert.ToString(row.GetValue(index), CultureInfo.InvariantCulture));
        }

        (await row.ReadAsync()).ShouldBeFalse();
        return values;
    }
}
