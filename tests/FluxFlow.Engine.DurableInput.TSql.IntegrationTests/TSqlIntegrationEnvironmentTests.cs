using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.TSql.IntegrationTests;

public sealed class TSqlIntegrationEnvironmentTests
{
    [Fact]
    public void Missing_environment_variable_fails_clearly_without_revealing_a_value()
    {
        var original = Environment.GetEnvironmentVariable(
            TSqlIntegrationEnvironment.ConnectionStringVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                TSqlIntegrationEnvironment.ConnectionStringVariable,
                null);

            var exception = Should.Throw<InvalidOperationException>(
                TSqlIntegrationEnvironment.RequireConfiguredConnectionString);

            exception.Message.ShouldBe(
                "The FLUXFLOW_TSQL_INTEGRATION_CONNECTION_STRING environment variable is required for the T-SQL integration tests.");
            exception.InnerException.ShouldBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                TSqlIntegrationEnvironment.ConnectionStringVariable,
                original);
        }
    }

    [Fact]
    public void Malformed_connection_setting_fails_with_stable_message_without_revealing_secret()
    {
        const string secret = "credential-sentinel-should-never-appear";
        var malformed = $"Server=localhost;Password={secret};Connect Timeout=not-a-number";

        var exception = Should.Throw<InvalidOperationException>(() =>
            TSqlIntegrationEnvironment.RequireConnectionString(malformed));

        exception.Message.ShouldBe("The T-SQL integration connection setting is malformed.");
        exception.ToString().ShouldNotContain(secret, Case.Insensitive);
        exception.InnerException.ShouldNotBeNull();
    }

    [Fact]
    public void Valid_connection_setting_defaults_master_and_bounds_connect_timeout()
    {
        var normalized = TSqlIntegrationEnvironment.RequireConnectionString(
            "Server=localhost;Integrated Security=true;Connect Timeout=60");
        var builder = new SqlConnectionStringBuilder(normalized);

        builder.DataSource.ShouldBe("localhost");
        builder.InitialCatalog.ShouldBe("master");
        builder.ConnectTimeout.ShouldBe(5);
        builder.IntegratedSecurity.ShouldBeTrue();
        normalized.ShouldNotContain("Password", Case.Insensitive);
    }
}
