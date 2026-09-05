using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.TSql.Tests;

public sealed class TSqlDurableInputStoreOptionsTests
{
    public static TheoryData<TimeSpan> ValidCommandTimeouts =>
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMinutes(10)
    ];

    public static TheoryData<TimeSpan> InvalidCommandTimeouts =>
    [
        TimeSpan.Zero,
        TimeSpan.FromTicks(-1),
        TimeSpan.FromSeconds(1).Add(TimeSpan.FromTicks(1)),
        TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1))
    ];

    public static TheoryData<TimeSpan> ValidSchemaLockTimeouts =>
    [
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(1),
        TimeSpan.FromMinutes(10)
    ];

    public static TheoryData<TimeSpan> InvalidSchemaLockTimeouts =>
    [
        TimeSpan.FromTicks(-1),
        TimeSpan.FromTicks(1),
        TimeSpan.FromMinutes(10).Add(TimeSpan.FromMilliseconds(1))
    ];

    public static TheoryData<TimeSpan> ValidConnectRetryIntervals =>
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(60)
    ];

    public static TheoryData<TimeSpan> InvalidConnectRetryIntervals =>
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1).Add(TimeSpan.FromTicks(1)),
        TimeSpan.FromSeconds(61)
    ];

    [Fact]
    public void Options_and_builder_expose_exact_defaults()
    {
        var options = new TSqlDurableInputStoreOptions();
        var builder = new TSqlDurableInputStoreOptionsBuilder();

        options.ConnectionString.ShouldBeNull();
        options.CommandTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.SchemaLockTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.ConnectRetryCount.ShouldBe(1);
        options.ConnectRetryInterval.ShouldBe(TimeSpan.FromSeconds(1));
        options.SchemaManagement.ShouldBe(TSqlDurableInputSchemaManagement.CreateOrMigrate);
        builder.ConnectionString.ShouldBeNull();
        builder.CommandTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        builder.SchemaLockTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        builder.ConnectRetryCount.ShouldBe(1);
        builder.ConnectRetryInterval.ShouldBe(TimeSpan.FromSeconds(1));
        builder.SchemaManagement.ShouldBe(TSqlDurableInputSchemaManagement.CreateOrMigrate);
    }

    [Theory]
    [MemberData(nameof(ValidCommandTimeouts))]
    public void Command_timeout_accepts_inclusive_whole_second_boundaries(TimeSpan value)
        => new TSqlDurableInputStoreOptions { CommandTimeout = value }.CommandTimeout.ShouldBe(value);

    [Theory]
    [MemberData(nameof(InvalidCommandTimeouts))]
    public void Command_timeout_rejects_out_of_range_or_fractional_seconds(TimeSpan value)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            new TSqlDurableInputStoreOptions { CommandTimeout = value });

        exception.ParamName.ShouldBe(nameof(TSqlDurableInputStoreOptions.CommandTimeout));
        exception.Message.ShouldContain("whole seconds");
    }

    [Theory]
    [MemberData(nameof(ValidSchemaLockTimeouts))]
    public void Schema_lock_timeout_accepts_zero_and_inclusive_whole_millisecond_boundaries(TimeSpan value)
        => new TSqlDurableInputStoreOptions { SchemaLockTimeout = value }.SchemaLockTimeout.ShouldBe(value);

    [Theory]
    [MemberData(nameof(InvalidSchemaLockTimeouts))]
    public void Schema_lock_timeout_rejects_out_of_range_or_fractional_milliseconds(TimeSpan value)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            new TSqlDurableInputStoreOptions { SchemaLockTimeout = value });

        exception.ParamName.ShouldBe(nameof(TSqlDurableInputStoreOptions.SchemaLockTimeout));
        exception.Message.ShouldContain("whole milliseconds");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Connect_retry_count_accepts_inclusive_boundaries(int value)
        => new TSqlDurableInputStoreOptions { ConnectRetryCount = value }.ConnectRetryCount.ShouldBe(value);

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void Connect_retry_count_rejects_values_outside_inclusive_range(int value)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            new TSqlDurableInputStoreOptions { ConnectRetryCount = value });

        exception.ParamName.ShouldBe(nameof(TSqlDurableInputStoreOptions.ConnectRetryCount));
        exception.Message.ShouldContain("between zero and five");
    }

    [Theory]
    [MemberData(nameof(ValidConnectRetryIntervals))]
    public void Connect_retry_interval_accepts_inclusive_whole_second_boundaries(TimeSpan value)
        => new TSqlDurableInputStoreOptions { ConnectRetryInterval = value }.ConnectRetryInterval.ShouldBe(value);

    [Theory]
    [MemberData(nameof(InvalidConnectRetryIntervals))]
    public void Connect_retry_interval_rejects_out_of_range_or_fractional_seconds(TimeSpan value)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            new TSqlDurableInputStoreOptions { ConnectRetryInterval = value });

        exception.ParamName.ShouldBe(nameof(TSqlDurableInputStoreOptions.ConnectRetryInterval));
        exception.Message.ShouldContain("whole seconds");
    }

    [Theory]
    [InlineData(TSqlDurableInputSchemaManagement.CreateOrMigrate)]
    [InlineData(TSqlDurableInputSchemaManagement.ValidateOnly)]
    public void Schema_management_accepts_every_defined_value(TSqlDurableInputSchemaManagement value)
        => new TSqlDurableInputStoreOptions { SchemaManagement = value }.SchemaManagement.ShouldBe(value);

    [Fact]
    public void Schema_management_rejects_undefined_values()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            new TSqlDurableInputStoreOptions
            {
                SchemaManagement = (TSqlDurableInputSchemaManagement)int.MaxValue
            });

        exception.ParamName.ShouldBe(nameof(TSqlDurableInputStoreOptions.SchemaManagement));
        exception.Message.ShouldContain("mode is invalid");
    }

    [Fact]
    public void Resolve_normalizes_connection_and_overrides_only_retry_settings()
    {
        const string raw =
            " Server=database.example.test;Database=FluxFlow;Application Name=Host App;" +
            "Encrypt=False;Connect Timeout=17;Connect Retry Count=5;Connect Retry Interval=60 ";
        var options = new TSqlDurableInputStoreOptions
        {
            ConnectionString = raw,
            CommandTimeout = TimeSpan.FromSeconds(9),
            SchemaLockTimeout = TimeSpan.FromMilliseconds(1250),
            ConnectRetryCount = 2,
            ConnectRetryInterval = TimeSpan.FromSeconds(7),
            SchemaManagement = TSqlDurableInputSchemaManagement.ValidateOnly
        };

        var settings = options.Resolve();
        var normalized = new SqlConnectionStringBuilder(settings.NormalizedConnectionString);

        options.ConnectionString.ShouldBe(raw.Trim());
        normalized.DataSource.ShouldBe("database.example.test");
        normalized.InitialCatalog.ShouldBe("FluxFlow");
        normalized.ApplicationName.ShouldBe("Host App");
        normalized["Encrypt"].ToString().ShouldBe("False");
        normalized.ConnectTimeout.ShouldBe(17);
        normalized.ConnectRetryCount.ShouldBe(2);
        normalized.ConnectRetryInterval.ShouldBe(7);
        settings.CommandTimeoutSeconds.ShouldBe(9);
        settings.SchemaLockTimeoutMilliseconds.ShouldBe(1250);
        settings.SchemaManagement.ShouldBe(TSqlDurableInputSchemaManagement.ValidateOnly);
    }

    [Theory]
    [InlineData(null, typeof(InvalidOperationException), "requires a connection string")]
    [InlineData("", typeof(InvalidOperationException), "requires a connection string")]
    [InlineData("   ", typeof(InvalidOperationException), "requires a connection string")]
    [InlineData("Database=FluxFlow", typeof(ArgumentException), "specify a server")]
    [InlineData("Server=database.example.test", typeof(ArgumentException), "specify a database")]
    public void Resolve_rejects_missing_connection_parts_without_opening(
        string? connectionString,
        Type exceptionType,
        string expectedMessage)
    {
        var options = new TSqlDurableInputStoreOptions { ConnectionString = connectionString };

        var exception = Should.Throw(() => new TSqlDurableInputStore(options), exceptionType);

        exception.Message.ShouldContain(expectedMessage);
    }

    [Fact]
    public void Resolve_rejects_malformed_connection_without_revealing_secret_or_opening()
    {
        const string secret = "Sensitive-Password-Sentinel";
        var options = new TSqlDurableInputStoreOptions
        {
            ConnectionString =
                $"Server=database.example.test;Database=FluxFlow;Password={secret};Unknown Keyword=value"
        };

        var exception = Should.Throw<ArgumentException>(() => new TSqlDurableInputStore(options));

        exception.ParamName.ShouldBe(nameof(TSqlDurableInputStoreOptions.ConnectionString));
        exception.Message.ShouldBe("T-SQL durable input connection string is invalid. (Parameter 'ConnectionString')");
        exception.ToString().ShouldNotContain(secret, Case.Insensitive);
    }

    [Fact]
    public void Builder_build_snapshots_every_value_into_immutable_options()
    {
        var builder = new TSqlDurableInputStoreOptionsBuilder
        {
            ConnectionString = " Server=database.example.test;Database=FluxFlow;Encrypt=False ",
            CommandTimeout = TimeSpan.FromSeconds(8),
            SchemaLockTimeout = TimeSpan.FromMilliseconds(3210),
            ConnectRetryCount = 4,
            ConnectRetryInterval = TimeSpan.FromSeconds(12),
            SchemaManagement = TSqlDurableInputSchemaManagement.ValidateOnly
        };

        var options = builder.Build();
        builder.ConnectionString = TSqlDurableInputTestData.UnreachableConnectionString;
        builder.CommandTimeout = TimeSpan.FromSeconds(1);
        builder.SchemaLockTimeout = TimeSpan.Zero;
        builder.ConnectRetryCount = 0;
        builder.ConnectRetryInterval = TimeSpan.FromSeconds(1);
        builder.SchemaManagement = TSqlDurableInputSchemaManagement.CreateOrMigrate;

        var normalized = new SqlConnectionStringBuilder(options.ConnectionString);
        normalized.DataSource.ShouldBe("database.example.test");
        normalized.InitialCatalog.ShouldBe("FluxFlow");
        normalized["Encrypt"].ToString().ShouldBe("False");
        normalized.ConnectRetryCount.ShouldBe(4);
        normalized.ConnectRetryInterval.ShouldBe(12);
        options.CommandTimeout.ShouldBe(TimeSpan.FromSeconds(8));
        options.SchemaLockTimeout.ShouldBe(TimeSpan.FromMilliseconds(3210));
        options.ConnectRetryCount.ShouldBe(4);
        options.ConnectRetryInterval.ShouldBe(TimeSpan.FromSeconds(12));
        options.SchemaManagement.ShouldBe(TSqlDurableInputSchemaManagement.ValidateOnly);
    }

    [Fact]
    public void Options_string_representation_redacts_connection_configuration()
    {
        const string secret = "Options-Secret-Sentinel";
        var options = new TSqlDurableInputStoreOptions
        {
            ConnectionString =
                $"Server=database.example.test;Database=FluxFlow;User ID=host;Password={secret}"
        };

        options.ToString().ShouldNotContain(secret, Case.Insensitive);
        options.ToString().ShouldNotContain("Password", Case.Insensitive);
        options.ToString().ShouldContain("[redacted]");
    }
}
