using Microsoft.Data.SqlClient;

namespace FluxFlow.Engine.DurableInput.TSql.IntegrationTests;

internal static class TSqlIntegrationEnvironment
{
    internal const string ConnectionStringVariable =
        "FLUXFLOW_TSQL_INTEGRATION_CONNECTION_STRING";

    private static readonly SemaphoreSlim ReadinessGate = new(1, 1);
    private static Task<string>? _readiness;

    internal static string RequireConfiguredConnectionString()
        => RequireConnectionString(Environment.GetEnvironmentVariable(ConnectionStringVariable));

    internal static string RequireConnectionString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"The {ConnectionStringVariable} environment variable is required for the T-SQL integration tests.");
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(value);
            if (string.IsNullOrWhiteSpace(builder.DataSource))
                throw new InvalidOperationException("The T-SQL integration server is missing.");
            if (string.IsNullOrWhiteSpace(builder.InitialCatalog))
                builder.InitialCatalog = "master";
            builder.ConnectTimeout = Math.Clamp(builder.ConnectTimeout, 1, 5);
            return builder.ConnectionString;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new InvalidOperationException(
                "The T-SQL integration connection setting is malformed.",
                exception);
        }
    }

    internal static async ValueTask<string> GetReadyConnectionStringAsync(
        CancellationToken cancellationToken = default)
    {
        var configured = RequireConfiguredConnectionString();
        if (_readiness is { } existing)
            return await existing.WaitAsync(cancellationToken).ConfigureAwait(false);

        await ReadinessGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _readiness ??= WaitUntilReadyAsync(configured);
            existing = _readiness;
        }
        finally
        {
            ReadinessGate.Release();
        }

        return await existing.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> WaitUntilReadyAsync(string connectionString)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        Exception? lastFailure = null;
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(timeout.Token).ConfigureAwait(false);
                return connectionString;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is SqlException or InvalidOperationException)
            {
                lastFailure = exception;
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        throw new InvalidOperationException(
            "The disposable T-SQL integration server did not become ready within 90 seconds.",
            lastFailure);
    }
}
