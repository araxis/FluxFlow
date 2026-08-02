using System.Data;
using Microsoft.Data.SqlClient;

namespace FluxFlow.Engine.DurableOutput.TSql.IntegrationTests;

internal sealed class TSqlTestDatabase : IAsyncDisposable
{
    private readonly List<TSqlDurableOutputStore> _stores = [];
    private readonly string _administrationConnectionString;
    private readonly SemaphoreSlim _storeGate = new(1, 1);
    private int _disposed;

    private TSqlTestDatabase(
        string name,
        string administrationConnectionString,
        string connectionString)
    {
        Name = name;
        _administrationConnectionString = administrationConnectionString;
        ConnectionString = connectionString;
    }

    internal string Name { get; }

    internal string ConnectionString { get; }

    internal static async ValueTask<TSqlTestDatabase> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        var administrationConnectionString =
            await TSqlIntegrationEnvironment.GetReadyConnectionStringAsync(cancellationToken)
                .ConfigureAwait(false);
        var name = "FluxFlowTSqlTests_" + Guid.NewGuid().ToString("N");
        ValidateName(name);

        await using (var connection = new SqlConnection(administrationConnectionString))
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                CREATE DATABASE [{name}];
                ALTER DATABASE [{name}] SET READ_COMMITTED_SNAPSHOT OFF;
                """;
            command.CommandTimeout = 30;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var builder = new SqlConnectionStringBuilder(administrationConnectionString)
        {
            InitialCatalog = name
        };
        return new TSqlTestDatabase(name, administrationConnectionString, builder.ConnectionString);
    }

    internal TSqlDurableOutputStore CreateStore(TSqlDurableOutputStoreOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var store = new TSqlDurableOutputStore(options ?? new TSqlDurableOutputStoreOptions
        {
            ConnectionString = ConnectionString
        });
        _storeGate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            _stores.Add(store);
            return store;
        }
        catch
        {
            store.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
        finally
        {
            _storeGate.Release();
        }
    }

    internal async ValueTask<SqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var connection = new SqlConnection(ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async ValueTask<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_administrationConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name;";
        command.Parameters.Add("@name", SqlDbType.NVarChar, 128).Value = Name;
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            System.Globalization.CultureInfo.InvariantCulture) == 1;
    }

    internal async ValueTask SetReadCommittedSnapshotAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(_administrationConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{Name}] SET READ_COMMITTED_SNAPSHOT {(enabled ? "ON" : "OFF")} WITH ROLLBACK IMMEDIATE;";
        command.CommandTimeout = 30;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await _storeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var store in _stores)
                await store.DisposeAsync().ConfigureAwait(false);
            _stores.Clear();
        }
        finally
        {
            _storeGate.Release();
            _storeGate.Dispose();
        }

        using (var pooledConnection = new SqlConnection(ConnectionString))
            SqlConnection.ClearPool(pooledConnection);

        await using var administration = new SqlConnection(_administrationConnectionString);
        await administration.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        await using var command = administration.CreateCommand();
        command.CommandText = $"""
            IF DB_ID(N'{Name}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{Name}];
            END;
            """;
        command.CommandTimeout = 30;
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static void ValidateName(string name)
    {
        if (name.Length > 128 || name.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character) || character == '_')))
        {
            throw new InvalidOperationException("Generated T-SQL integration database name is invalid.");
        }
    }
}
