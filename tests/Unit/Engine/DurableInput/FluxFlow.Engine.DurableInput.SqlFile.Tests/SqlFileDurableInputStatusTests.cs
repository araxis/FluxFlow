using FluxFlow.Engine.DurableInput.Tests;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

public sealed class SqlFileDurableInputStatusTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 8, 1, 16, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task Missing_database_status_creates_no_directory_or_file()
    {
        using var database = TemporarySqliteDatabase.Create();
        var directory = Path.Combine(database.DirectoryPath, "missing", "nested");
        var path = Path.Combine(directory, "status.db");
        await using var store = CreateStore(path);

        var exception = await Should.ThrowAsync<FileNotFoundException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());

        exception.FileName.ShouldBe(path);
        Directory.Exists(directory).ShouldBeFalse();
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Precancelled_missing_database_status_creates_no_directory_or_file()
    {
        using var database = TemporarySqliteDatabase.Create();
        var directory = Path.Combine(database.DirectoryPath, "cancelled", "nested");
        var path = Path.Combine(directory, "status.db");
        await using var store = CreateStore(path);
        using var source = new CancellationTokenSource();
        source.Cancel();

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            store.GetStatusAsync(new(ObservedAt), source.Token).AsTask());

        exception.CancellationToken.ShouldBe(source.Token);
        Directory.Exists(directory).ShouldBeFalse();
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Status_ignores_corrupt_payload_columns_and_returns_exact_metadata()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore();
        var envelope = DurableInputStoreConformanceData.Envelope(
            "status-corrupt-payload",
            enqueuedAt: ObservedAt.AddMinutes(-5));
        await store.EnqueueAsync(envelope);
        await ExecuteAsync(
            database.DatabasePath,
            "UPDATE fluxflow_durable_inputs SET payload_json = 'not-json', headers_json = 'also-not-json';");

        var snapshot = await store.GetStatusAsync(new(ObservedAt));

        snapshot.PendingCount.ShouldBe(1);
        snapshot.ReadyPendingCount.ShouldBe(1);
        snapshot.TotalCount.ShouldBe(1);
        snapshot.OldestReadyAt.ShouldBe(ObservedAt.AddMinutes(-5));
    }

    [Fact]
    public async Task Undefined_state_status_fails_visibly_without_mutation()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore();
        await store.EnqueueAsync(DurableInputStoreConformanceData.Envelope(
            "status-invalid-state",
            enqueuedAt: ObservedAt));
        await ExecuteAsync(
            database.DatabasePath,
            "PRAGMA ignore_check_constraints = ON; UPDATE fluxflow_durable_inputs SET state = 99;");

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());
        var state = await ScalarAsync<long>(database.DatabasePath, "SELECT state FROM fluxflow_durable_inputs;");

        exception.Message.ShouldContain("invalid state");
        state.ShouldBe(99);
    }

    [Fact]
    public async Task Busy_status_times_out_safely_then_recovers_with_the_exact_snapshot()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore(busyTimeout: TimeSpan.FromMilliseconds(100));
        await store.EnqueueAsync(DurableInputStoreConformanceData.Envelope(
            "status-busy",
            enqueuedAt: ObservedAt.AddMinutes(-1)));
        await using var lockConnection = await OpenAsync(database.DatabasePath);
        await ExecuteAsync(lockConnection, "BEGIN EXCLUSIVE;");

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());
        await ExecuteAsync(lockConnection, "ROLLBACK;");
        var recovered = await store.GetStatusAsync(new(ObservedAt));

        exception.Message.ShouldContain("status inspection");
        exception.Message.ShouldContain("configured busy timeout");
        recovered.PendingCount.ShouldBe(1);
        recovered.ReadyPendingCount.ShouldBe(1);
        recovered.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Reopened_store_returns_the_same_snapshot_without_schema_mutation()
    {
        using var database = TemporarySqliteDatabase.Create();
        DurableInputStatusSnapshot first;
        string schemaBefore;
        await using (var store = database.CreateStore())
        {
            await store.EnqueueAsync(DurableInputStoreConformanceData.Envelope(
                "status-reopen",
                enqueuedAt: ObservedAt.AddMinutes(-3)));
            schemaBefore = await SchemaFingerprintAsync(database.DatabasePath);
            first = await store.GetStatusAsync(new(ObservedAt));
        }

        await using var reopened = database.CreateStore(createDatabase: false);
        var second = await reopened.GetStatusAsync(new(ObservedAt));
        var schemaAfter = await SchemaFingerprintAsync(database.DatabasePath);

        second.ShouldBe(first);
        schemaAfter.ShouldBe(schemaBefore);
    }

    [Fact]
    public async Task Disposed_store_rejects_status_without_reopening_the_database()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = database.CreateStore();
        await store.EnqueueAsync(DurableInputStoreConformanceData.Envelope("status-disposed"));
        await store.DisposeAsync();

        var exception = await Should.ThrowAsync<ObjectDisposedException>(() =>
            store.GetStatusAsync(new(ObservedAt)).AsTask());

        exception.ObjectName.ShouldBe(typeof(SqlFileDurableInputStore).FullName);
        File.Exists(database.DatabasePath).ShouldBeTrue();
    }

    private static SqlFileDurableInputStore CreateStore(string path)
        => new(new SqlFileDurableInputStoreOptions
        {
            DatabasePath = path,
            AllowAbsoluteDatabasePath = true,
            CreateDatabase = true,
            CreateDirectory = true
        });

    private static async ValueTask ExecuteAsync(string path, string commandText)
    {
        await using var connection = await OpenAsync(path);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static async ValueTask ExecuteAsync(SqliteConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static async ValueTask<T> ScalarAsync<T>(string path, string commandText)
    {
        await using var connection = await OpenAsync(path);
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var value = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(value!, typeof(T));
    }

    private static ValueTask<string> SchemaFingerprintAsync(string path)
        => ScalarAsync<string>(
            path,
            """
            SELECT group_concat(type || ':' || name || ':' || COALESCE(sql, ''), char(10))
            FROM (SELECT type, name, sql FROM sqlite_master ORDER BY type, name);
            """);

    private static async ValueTask<SqliteConnection> OpenAsync(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        return connection;
    }
}
