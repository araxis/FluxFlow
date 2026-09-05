using System.Diagnostics;
using FluxFlow.Engine.DurableInput.Tests;
using Microsoft.Data.Sqlite;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

public sealed class SqlFileDurableInputStoreSchemaAndLifecycleTests
{
    [Fact]
    public async Task First_operation_creates_directory_database_and_explicit_version_two_schema()
    {
        using var database = TemporarySqliteDatabase.Create();
        var nestedDirectory = Path.Combine(database.DirectoryPath, "nested");
        var path = Path.Combine(nestedDirectory, "durable-input.db");
        await using var store = CreateStore(path);

        var leases = await store.LeaseAsync(DurableInputStoreConformanceData.Request());

        leases.ShouldBeEmpty();
        Directory.Exists(nestedDirectory).ShouldBeTrue();
        File.Exists(path).ShouldBeTrue();
        await using var connection = await OpenAsync(path);
        (await ScalarAsync<long>(
            connection,
            "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;"))
            .ShouldBe(2);
        var schemaObjects = await ReadStringsAsync(
            connection,
            """
            SELECT name
            FROM sqlite_schema
            WHERE name LIKE 'fluxflow_durable_input%'
               OR name LIKE 'ix_fluxflow_durable_input%'
            ORDER BY name;
            """);
        schemaObjects.ShouldBe([
            "fluxflow_durable_input_schema",
            "fluxflow_durable_inputs",
            "ix_fluxflow_durable_inputs_dead_lettered",
            "ix_fluxflow_durable_inputs_lease_expiry",
            "ix_fluxflow_durable_inputs_pending_due"
        ]);
        await SqlFileDeadLetterSchemaAssertions.ShouldHaveExactVersionTwoShapeAsync(path);
    }

    [Fact]
    public async Task Missing_directory_is_rejected_on_first_use_when_directory_creation_is_disabled()
    {
        using var database = TemporarySqliteDatabase.Create();
        var nestedDirectory = Path.Combine(database.DirectoryPath, "missing");
        var path = Path.Combine(nestedDirectory, "durable-input.db");
        await using var store = CreateStore(path, createDirectory: false);

        var exception = await Should.ThrowAsync<DirectoryNotFoundException>(
            () => store.LeaseAsync(DurableInputStoreConformanceData.Request()).AsTask());

        exception.Message.ShouldContain(nestedDirectory);
        Directory.Exists(nestedDirectory).ShouldBeFalse();
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Missing_database_is_rejected_on_first_use_when_database_creation_is_disabled()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var store = database.CreateStore(createDatabase: false);

        var exception = await Should.ThrowAsync<FileNotFoundException>(
            () => store.LeaseAsync(DurableInputStoreConformanceData.Request()).AsTask());

        exception.FileName.ShouldBe(database.DatabasePath);
        exception.Message.ShouldContain(database.DatabasePath);
        File.Exists(database.DatabasePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Existing_database_reopens_when_directory_and_database_creation_are_disabled()
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableInputStoreConformanceData.Envelope(messageId: "existing-database");
        await using (var initializer = database.CreateStore())
        {
            (await initializer.EnqueueAsync(envelope))
                .Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
        }

        await using var reader = database.CreateStore(
            createDatabase: false,
            createDirectory: false);
        var lease = (await reader.LeaseAsync(DurableInputStoreConformanceData.Request())).Single();

        lease.Envelope.ShouldMatchEnvelope(envelope);
        lease.Attempt.ShouldBe(1);
    }

    [Fact]
    public async Task Precancelled_first_operation_creates_neither_directory_nor_database()
    {
        using var database = TemporarySqliteDatabase.Create();
        var nestedDirectory = Path.Combine(database.DirectoryPath, "cancelled");
        var path = Path.Combine(nestedDirectory, "durable-input.db");
        await using var store = CreateStore(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => store.EnqueueAsync(
                DurableInputStoreConformanceData.Envelope(),
                cancellation.Token).AsTask());

        Directory.Exists(nestedDirectory).ShouldBeFalse();
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Store_rejects_null_options_and_requests_before_database_io()
    {
        using var database = TemporarySqliteDatabase.Create();
        var optionsException = Should.Throw<ArgumentNullException>(() =>
            new SqlFileDurableInputStore(null!));
        await using var store = database.CreateStore();

        var enqueueException = await Should.ThrowAsync<ArgumentNullException>(
            () => store.EnqueueAsync(null!).AsTask());
        var leaseException = await Should.ThrowAsync<ArgumentNullException>(
            () => store.LeaseAsync(null!).AsTask());
        var deliveredException = await Should.ThrowAsync<ArgumentNullException>(
            () => store.MarkDeliveredAsync(null!).AsTask());
        var renewalException = await Should.ThrowAsync<ArgumentNullException>(
            () => store.RenewLeaseAsync(null!).AsTask());
        var releaseException = await Should.ThrowAsync<ArgumentNullException>(
            () => store.ReleaseAsync(null!).AsTask());
        var deadLetterException = await Should.ThrowAsync<ArgumentNullException>(
            () => store.DeadLetterAsync(null!).AsTask());
        var listException = await Should.ThrowAsync<ArgumentNullException>(
            () => store.ListAsync(null!).AsTask());
        var getException = await Should.ThrowAsync<ArgumentException>(
            () => store.GetAsync(default).AsTask());
        var replayException = await Should.ThrowAsync<ArgumentNullException>(
            () => store.ReplayAsync(null!).AsTask());

        optionsException.ParamName.ShouldBe("options");
        enqueueException.ParamName.ShouldBe("envelope");
        leaseException.ParamName.ShouldBe("request");
        deliveredException.ParamName.ShouldBe("transition");
        renewalException.ParamName.ShouldBe("renewal");
        releaseException.ParamName.ShouldBe("release");
        deadLetterException.ParamName.ShouldBe("deadLetter");
        listException.ParamName.ShouldBe("query");
        getException.ParamName.ShouldBe("key");
        replayException.ParamName.ShouldBe("replay");
        File.Exists(database.DatabasePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Concurrent_first_use_initializes_one_complete_schema_for_both_instances()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using var first = database.CreateStore();
        await using var second = database.CreateStore();
        var firstRequest = DurableInputStoreConformanceData.Request(ownerId: "initializer-a");
        var secondRequest = DurableInputStoreConformanceData.Request(ownerId: "initializer-b");

        var results = await Task.WhenAll(
            first.LeaseAsync(firstRequest).AsTask(),
            second.LeaseAsync(secondRequest).AsTask());

        results.ShouldAllBe(static leases => leases.Count == 0);
        await using var connection = await OpenAsync(database.DatabasePath);
        (await ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM fluxflow_durable_input_schema WHERE singleton = 1 AND version = 2;"))
            .ShouldBe(1);
        (await ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'fluxflow_durable_inputs';"))
            .ShouldBe(1);
        (await ScalarAsync<long>(
            connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'index' AND name = 'ix_fluxflow_durable_inputs_dead_lettered';"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Future_schema_version_is_rejected_without_downgrade()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using (var initializer = database.CreateStore())
        {
            (await initializer.LeaseAsync(DurableInputStoreConformanceData.Request()))
                .ShouldBeEmpty();
        }

        await using (var connection = await OpenAsync(database.DatabasePath))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "UPDATE fluxflow_durable_input_schema SET version = 3 WHERE singleton = 1;";
            (await command.ExecuteNonQueryAsync()).ShouldBe(1);
        }

        await using var reader = database.CreateStore();
        var exception = await Should.ThrowAsync<NotSupportedException>(
            () => reader.LeaseAsync(DurableInputStoreConformanceData.Request()).AsTask());

        exception.Message.ShouldContain("schema version 3");
        exception.Message.ShouldContain("supported version 2");
        await using var verification = await OpenAsync(database.DatabasePath);
        (await ScalarAsync<long>(
            verification,
            "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;"))
            .ShouldBe(3);
    }

    [Fact]
    public async Task Unversioned_durable_input_table_is_rejected_without_adopting_it()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE fluxflow_durable_inputs (foreign_value TEXT NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        await using var store = database.CreateStore();
        var exception = await Should.ThrowAsync<InvalidDataException>(
            () => store.LeaseAsync(DurableInputStoreConformanceData.Request()).AsTask());

        exception.Message.ShouldContain("unversioned durable-input table");
        await using var verification = await OpenAsync(database.DatabasePath);
        (await ScalarAsync<long>(
            verification,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'fluxflow_durable_input_schema';"))
            .ShouldBe(0);
        (await ScalarAsync<long>(
            verification,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name = 'fluxflow_durable_inputs';"))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Version_metadata_without_the_durable_input_table_is_rejected()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE fluxflow_durable_input_schema (
                    singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 1),
                    version INTEGER NOT NULL CHECK (version > 0)
                ) WITHOUT ROWID;
                INSERT INTO fluxflow_durable_input_schema (singleton, version) VALUES (1, 1);
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var store = database.CreateStore();
        var exception = await Should.ThrowAsync<InvalidDataException>(
            () => store.LeaseAsync(DurableInputStoreConformanceData.Request()).AsTask());

        exception.Message.ShouldContain("metadata exists");
        exception.Message.ShouldContain("table is missing");
    }

    [Fact]
    public async Task Older_schema_without_a_migration_is_rejected_without_upgrade()
    {
        using var database = TemporarySqliteDatabase.Create();
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE fluxflow_durable_input_schema (
                    singleton INTEGER NOT NULL PRIMARY KEY,
                    version INTEGER NOT NULL
                ) WITHOUT ROWID;
                INSERT INTO fluxflow_durable_input_schema (singleton, version) VALUES (1, 0);
                CREATE TABLE fluxflow_durable_inputs (foreign_value TEXT NOT NULL);
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var store = database.CreateStore();
        var exception = await Should.ThrowAsync<NotSupportedException>(
            () => store.LeaseAsync(DurableInputStoreConformanceData.Request()).AsTask());

        exception.Message.ShouldContain("schema version 0");
        exception.Message.ShouldContain("cannot be migrated");
        await using var verification = await OpenAsync(database.DatabasePath);
        (await ScalarAsync<long>(
            verification,
            "SELECT version FROM fluxflow_durable_input_schema WHERE singleton = 1;"))
            .ShouldBe(0);
    }

    [Theory]
    [InlineData(LockedStoreOperation.Enqueue)]
    [InlineData(LockedStoreOperation.Lease)]
    [InlineData(LockedStoreOperation.Transition)]
    [InlineData(LockedStoreOperation.Renewal)]
    public async Task External_write_lock_honors_busy_timeout_then_store_recovers_after_release(
        LockedStoreOperation operation)
    {
        using var database = TemporarySqliteDatabase.Create();
        var timeout = TimeSpan.FromMilliseconds(150);
        await using var store = database.CreateStore(busyTimeout: timeout);
        (await store.LeaseAsync(DurableInputStoreConformanceData.Request())).ShouldBeEmpty();
        var lockedEnvelope = DurableInputStoreConformanceData.Envelope(messageId: "locked");
        DurableInputLease? lockedLease = null;
        if (operation is LockedStoreOperation.Transition or LockedStoreOperation.Renewal)
        {
            await store.EnqueueAsync(lockedEnvelope);
            lockedLease = (await store.LeaseAsync(
                DurableInputStoreConformanceData.Request())).Single();
        }

        await using var lockConnection = await OpenAsync(database.DatabasePath);
        await using var writeLock = lockConnection.BeginTransaction(deferred: false);
        var stopwatch = Stopwatch.StartNew();

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            InvokeLockedOperationAsync);
        stopwatch.Stop();

        exception.Message.ShouldContain(operation switch
        {
            LockedStoreOperation.Enqueue => "enqueue",
            LockedStoreOperation.Lease => "lease",
            LockedStoreOperation.Transition => "transition",
            LockedStoreOperation.Renewal => "lease renewal",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        });
        exception.Message.ShouldContain("configured busy timeout");
        exception.Message.ShouldContain(timeout.ToString());
        exception.InnerException.ShouldBeOfType<SqliteException>();
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100));
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5));

        await writeLock.RollbackAsync();
        switch (operation)
        {
            case LockedStoreOperation.Enqueue:
                (await store.EnqueueAsync(lockedEnvelope))
                    .Status.ShouldBe(DurableInputEnqueueStatus.Enqueued);
                break;
            case LockedStoreOperation.Lease:
                (await store.LeaseAsync(DurableInputStoreConformanceData.Request()))
                    .ShouldBeEmpty();
                break;
            case LockedStoreOperation.Transition:
                (await store.MarkDeliveredAsync(new DurableInputLeaseTransition(
                    lockedEnvelope.Key,
                    lockedLease!.LeaseToken,
                    DurableInputStoreConformanceData.Now.AddSeconds(1))))
                    .Status.ShouldBe(DurableInputTransitionStatus.Applied);
                break;
            case LockedStoreOperation.Renewal:
                (await store.RenewLeaseAsync(new DurableInputLeaseRenewal(
                    lockedEnvelope.Key,
                    lockedLease!.LeaseToken,
                    DurableInputStoreConformanceData.Now.AddSeconds(1),
                    DurableInputStoreConformanceData.Now.AddMinutes(1))))
                    .Status.ShouldBe(DurableInputTransitionStatus.Applied);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }

        async Task InvokeLockedOperationAsync()
        {
            switch (operation)
            {
                case LockedStoreOperation.Enqueue:
                    await store.EnqueueAsync(lockedEnvelope);
                    break;
                case LockedStoreOperation.Lease:
                    await store.LeaseAsync(DurableInputStoreConformanceData.Request());
                    break;
                case LockedStoreOperation.Transition:
                    await store.MarkDeliveredAsync(new DurableInputLeaseTransition(
                        lockedEnvelope.Key,
                        lockedLease!.LeaseToken,
                        DurableInputStoreConformanceData.Now.AddSeconds(1)));
                    break;
                case LockedStoreOperation.Renewal:
                    await store.RenewLeaseAsync(new DurableInputLeaseRenewal(
                        lockedEnvelope.Key,
                        lockedLease!.LeaseToken,
                        DurableInputStoreConformanceData.Now.AddSeconds(1),
                        DurableInputStoreConformanceData.Now.AddMinutes(1)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }
    }

    [Theory]
    [InlineData("payload_json", "payload")]
    [InlineData("headers_json", "headers")]
    public async Task Corrupt_persisted_json_is_rejected_with_row_and_field_context(
        string column,
        string field)
    {
        using var database = TemporarySqliteDatabase.Create();
        var envelope = DurableInputStoreConformanceData.Envelope(messageId: $"corrupt-{field}");
        await using (var writer = database.CreateStore())
        {
            await writer.EnqueueAsync(envelope);
        }

        await using (var connection = await OpenAsync(database.DatabasePath))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "PRAGMA ignore_check_constraints = ON; " +
                "UPDATE fluxflow_durable_inputs " +
                $"SET {column} = '{{not-json' " +
                "WHERE message_id = $messageId;";
            command.Parameters.AddWithValue("$messageId", envelope.MessageId.Value);
            (await command.ExecuteNonQueryAsync()).ShouldBe(1);
        }

        await using var reader = database.CreateStore();
        var exception = await Should.ThrowAsync<InvalidDataException>(
            () => reader.LeaseAsync(DurableInputStoreConformanceData.Request()).AsTask());

        exception.Message.ShouldContain(envelope.Key.ToString());
        exception.Message.ShouldContain(field);
        exception.Message.ShouldContain("JSON");
    }

    [Fact]
    public async Task Dispose_is_idempotent_rejects_later_operations_and_releases_the_database_file()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = database.CreateStore();
        await store.EnqueueAsync(DurableInputStoreConformanceData.Envelope());

        await store.DisposeAsync();
        await store.DisposeAsync();
        var exception = await Should.ThrowAsync<ObjectDisposedException>(
            () => store.LeaseAsync(DurableInputStoreConformanceData.Request()).AsTask());
        var renewalException = await Should.ThrowAsync<ObjectDisposedException>(
            () => store.RenewLeaseAsync(new DurableInputLeaseRenewal(
                DurableInputStoreConformanceData.Envelope().Key,
                Guid.NewGuid(),
                DurableInputStoreConformanceData.Now,
                DurableInputStoreConformanceData.Now.AddMinutes(1))).AsTask());
        File.Delete(database.DatabasePath);

        exception.ObjectName.ShouldContain(nameof(SqlFileDurableInputStore));
        renewalException.ObjectName.ShouldContain(nameof(SqlFileDurableInputStore));
        File.Exists(database.DatabasePath).ShouldBeFalse();
    }

    private static SqlFileDurableInputStore CreateStore(
        string path,
        bool createDatabase = true,
        bool createDirectory = true)
        => new(new SqlFileDurableInputStoreOptions
        {
            DatabasePath = path,
            AllowAbsoluteDatabasePath = true,
            CreateDatabase = createDatabase,
            CreateDirectory = createDirectory
        });

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

    private static async ValueTask<T> ScalarAsync<T>(
        SqliteConnection connection,
        string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var result = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    private static async ValueTask<IReadOnlyList<string>> ReadStringsAsync(
        SqliteConnection connection,
        string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    public enum LockedStoreOperation
    {
        Enqueue,
        Lease,
        Transition,
        Renewal
    }
}
