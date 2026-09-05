using System.Diagnostics;
using FluxFlow.Engine.DurableOutput.Tests;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputStoreSchemaAndLifecycleTests
{
    [Fact]
    public async Task Constructor_is_lazy_and_first_enqueue_creates_directory_database_and_exact_version_one_schema()
    {
        using var database = TemporarySqliteDatabase.Create();
        var nestedDirectory = Path.Combine(database.DirectoryPath, "nested");
        var path = Path.Combine(nestedDirectory, "durable-output.db");
        await using var store = CreateStore(path);

        Directory.Exists(nestedDirectory).ShouldBeFalse();
        File.Exists(path).ShouldBeFalse();
        var result = await store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope());

        result.Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
        Directory.Exists(nestedDirectory).ShouldBeTrue();
        File.Exists(path).ShouldBeTrue();
        await using var connection = await SqlFileDurableOutputTestDatabase.OpenAsync(path);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            connection,
            "SELECT version FROM fluxflow_durable_output_schema WHERE singleton = 1;"))
            .ShouldBe(1);
        var objects = await SqlFileDurableOutputTestDatabase.ReadStringsAsync(
            connection,
            """
            SELECT name
            FROM sqlite_schema
            WHERE name LIKE 'fluxflow_durable_output%'
            ORDER BY name;
            """);
        objects.ShouldBe([
            "fluxflow_durable_output_schema",
            "fluxflow_durable_outputs"
        ]);
        (await ReadColumnShapeAsync(connection, "fluxflow_durable_output_schema"))
            .ShouldBe([
                "0|singleton|INTEGER|1|1",
                "1|version|INTEGER|1|0"
            ]);
        (await ReadColumnShapeAsync(connection, "fluxflow_durable_outputs"))
            .ShouldBe([
                "0|application_address|TEXT|1|1",
                "1|message_id|TEXT|1|2",
                "2|contract_name|TEXT|1|0",
                "3|envelope_schema_version|INTEGER|1|0",
                "4|is_error|INTEGER|1|0",
                "5|payload_json|TEXT|1|0",
                "6|error_code|TEXT|0|0",
                "7|error_message|TEXT|0|0",
                "8|error_category|TEXT|0|0",
                "9|error_is_transient|INTEGER|0|0",
                "10|error_details_json|TEXT|0|0",
                "11|trace_id|TEXT|1|0",
                "12|correlation_id|TEXT|0|0",
                "13|causation_id|TEXT|0|0",
                "14|message_timestamp_utc_ticks|INTEGER|1|0",
                "15|message_timestamp_offset_minutes|INTEGER|1|0",
                "16|captured_at_utc_ticks|INTEGER|1|0",
                "17|captured_at_offset_minutes|INTEGER|1|0",
                "18|headers_json|TEXT|1|0"
            ]);
        var outputSql = await SqlFileDurableOutputTestDatabase.ScalarAsync<string>(
            connection,
            "SELECT sql FROM sqlite_schema WHERE name = 'fluxflow_durable_outputs';");
        outputSql.ShouldContain("WITHOUT ROWID");
        outputSql.ShouldContain("PRIMARY KEY (application_address, message_id)");
        outputSql.ShouldContain("message_timestamp_offset_minutes BETWEEN -840 AND 840");
        outputSql.ShouldContain("captured_at_offset_minutes BETWEEN -840 AND 840");
        outputSql.ShouldContain("is_error = 1");
        outputSql.ShouldContain("payload_json = 'null'");
    }

    [Fact]
    public async Task Missing_directory_is_rejected_on_first_use_when_directory_creation_is_disabled()
    {
        using var database = TemporarySqliteDatabase.Create();
        var nestedDirectory = Path.Combine(database.DirectoryPath, "missing");
        var path = Path.Combine(nestedDirectory, "durable-output.db");
        await using var store = CreateStore(path, createDirectory: false);

        var exception = await Should.ThrowAsync<DirectoryNotFoundException>(() =>
            store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope()).AsTask());

        exception.Message.ShouldContain(nestedDirectory);
        Directory.Exists(nestedDirectory).ShouldBeFalse();
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Missing_database_is_rejected_on_first_use_when_database_creation_is_disabled()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore(createDatabase: false);

        var exception = await Should.ThrowAsync<FileNotFoundException>(() =>
            store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope()).AsTask());

        exception.FileName.ShouldBe(database.DatabasePath);
        File.Exists(database.DatabasePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Newer_schema_version_is_rejected_without_mutation()
    {
        using var database = TemporarySqliteDatabase.Create();
        await CreateVersionMetadataAsync(database.DatabasePath, version: 2);
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<NotSupportedException>(() =>
            store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope()).AsTask());

        exception.Message.ShouldContain("schema version 2");
        exception.Message.ShouldContain("supported version 1");
        await using var verification = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            verification,
            "SELECT version FROM fluxflow_durable_output_schema WHERE singleton = 1;"))
            .ShouldBe(2);
    }

    [Fact]
    public async Task Unversioned_output_table_is_rejected_without_adoption()
    {
        using var database = TemporarySqliteDatabase.Create();
        await SqlFileDurableOutputTestDatabase.ExecuteAsync(
            database.DatabasePath,
            "CREATE TABLE fluxflow_durable_outputs (foreign_value TEXT NOT NULL);");
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope()).AsTask());

        exception.Message.ShouldContain("unversioned durable-output table");
        await using var verification = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await SqlFileDurableOutputTestDatabase.ScalarAsync<long>(
            verification,
            "SELECT COUNT(*) FROM sqlite_schema WHERE name = 'fluxflow_durable_output_schema';"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Version_metadata_without_output_table_is_rejected()
    {
        using var database = TemporarySqliteDatabase.Create();
        await CreateVersionMetadataAsync(database.DatabasePath, version: 1);
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope()).AsTask());

        exception.Message.ShouldContain("missing required table");
        exception.Message.ShouldContain("fluxflow_durable_outputs");
    }

    [Fact]
    public async Task Missing_version_row_is_rejected_as_corrupt_metadata()
    {
        using var database = TemporarySqliteDatabase.Create();
        await CreateVersionMetadataAsync(database.DatabasePath, version: null);
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope()).AsTask());

        exception.Message.ShouldContain("schema version is missing");
    }

    [Fact]
    public async Task Incompatible_output_table_shape_is_rejected_without_repair()
    {
        using var database = TemporarySqliteDatabase.Create();
        await CreateVersionMetadataAsync(database.DatabasePath, version: 1);
        await SqlFileDurableOutputTestDatabase.ExecuteAsync(
            database.DatabasePath,
            "CREATE TABLE fluxflow_durable_outputs (foreign_value TEXT NOT NULL);");
        await using var store = database.CreateStore();

        var exception = await Should.ThrowAsync<InvalidDataException>(() =>
            store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope()).AsTask());

        exception.Message.ShouldContain("incompatible column count");
        await using var verification = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        (await ReadColumnShapeAsync(verification, "fluxflow_durable_outputs"))
            .ShouldBe(["0|foreign_value|TEXT|1|0"]);
    }

    [Fact]
    public async Task External_write_lock_honors_busy_timeout_then_store_recovers_after_release()
    {
        using var database = TemporarySqliteDatabase.Create();
        var timeout = TimeSpan.FromMilliseconds(150);
        await using var store = database.CreateStore(busyTimeout: timeout);
        await store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope(messageId: "seed"));
        await using var lockConnection = await SqlFileDurableOutputTestDatabase.OpenAsync(
            database.DatabasePath);
        await using var writeLock = lockConnection.BeginTransaction(deferred: false);
        var blocked = DurableOutputStoreConformanceData.Envelope(messageId: "blocked");
        var stopwatch = Stopwatch.StartNew();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            store.EnqueueAsync(blocked).AsTask());
        stopwatch.Stop();

        exception.Message.ShouldContain("enqueue");
        exception.Message.ShouldContain("configured busy timeout");
        exception.Message.ShouldContain(timeout.ToString());
        exception.InnerException.ShouldBeOfType<SqliteException>();
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));
        await writeLock.RollbackAsync();
        (await SqlFileDurableOutputTestDatabase.ReadOutputAsync(
            database.DatabasePath,
            blocked.Key)).ShouldBeNull();
        (await store.EnqueueAsync(blocked)).Status.ShouldBe(DurableOutputEnqueueStatus.Enqueued);
    }

    [Fact]
    public async Task Dispose_is_idempotent_rejects_later_operations_and_releases_database_file()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = database.CreateStore();
        await store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope());

        await store.DisposeAsync();
        await store.DisposeAsync();
        var exception = await Should.ThrowAsync<ObjectDisposedException>(() =>
            store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope()).AsTask());
        File.Delete(database.DatabasePath);

        exception.ObjectName.ShouldContain(nameof(SqlFileDurableOutputStore));
        File.Exists(database.DatabasePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Dispose_before_first_use_performs_no_io_and_rejects_later_enqueue()
    {
        using var database = TemporarySqliteDatabase.Create();
        var nestedDirectory = Path.Combine(database.DirectoryPath, "never-created");
        var path = Path.Combine(nestedDirectory, "durable-output.db");
        var store = CreateStore(path);

        await store.DisposeAsync();
        var exception = await Should.ThrowAsync<ObjectDisposedException>(() =>
            store.EnqueueAsync(DurableOutputStoreConformanceData.Envelope()).AsTask());

        exception.ObjectName.ShouldContain(nameof(SqlFileDurableOutputStore));
        Directory.Exists(nestedDirectory).ShouldBeFalse();
        File.Exists(path).ShouldBeFalse();
    }

    private static SqlFileDurableOutputStore CreateStore(
        string path,
        bool createDatabase = true,
        bool createDirectory = true)
        => new(new SqlFileDurableOutputStoreOptions
        {
            DatabasePath = path,
            AllowAbsoluteDatabasePath = true,
            CreateDatabase = createDatabase,
            CreateDirectory = createDirectory
        });

    private static ValueTask CreateVersionMetadataAsync(string path, int? version)
        => SqlFileDurableOutputTestDatabase.ExecuteAsync(
            path,
            """
            CREATE TABLE fluxflow_durable_output_schema (
                singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 1),
                version INTEGER NOT NULL CHECK (version > 0)
            ) WITHOUT ROWID;
            """ + (version is null
                ? string.Empty
                : $"INSERT INTO fluxflow_durable_output_schema (singleton, version) VALUES (1, {version.Value});"));

    private static async ValueTask<IReadOnlyList<string>> ReadColumnShapeAsync(
        SqliteConnection connection,
        string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{table}');";
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<string>();
        while (await reader.ReadAsync())
        {
            columns.Add($"{reader.GetInt32(0)}|{reader.GetString(1)}|{reader.GetString(2)}|" +
                $"{reader.GetInt32(3)}|{reader.GetInt32(5)}");
        }

        return columns;
    }
}
