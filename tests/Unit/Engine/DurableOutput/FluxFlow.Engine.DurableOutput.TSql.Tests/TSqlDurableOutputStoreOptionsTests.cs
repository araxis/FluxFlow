using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.TSql.Tests;

public sealed class TSqlDurableOutputStoreOptionsTests
{
    public static TheoryData<TimeSpan> ValidCommandTimeouts => new()
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromMinutes(10)
    };

    public static TheoryData<TimeSpan> InvalidCommandTimeouts => new()
    {
        TimeSpan.Zero,
        TimeSpan.FromTicks(-1),
        TimeSpan.FromSeconds(1).Add(TimeSpan.FromTicks(1)),
        TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(1))
    };

    public static TheoryData<TimeSpan> ValidSchemaLockTimeouts => new()
    {
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(1),
        TimeSpan.FromMinutes(10)
    };

    public static TheoryData<TimeSpan> InvalidSchemaLockTimeouts => new()
    {
        TimeSpan.FromTicks(-1),
        TimeSpan.FromTicks(1),
        TimeSpan.FromMinutes(10).Add(TimeSpan.FromMilliseconds(1))
    };

    public static TheoryData<TimeSpan> ValidConnectRetryIntervals => new()
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(60)
    };

    public static TheoryData<TimeSpan> InvalidConnectRetryIntervals => new()
    {
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1).Add(TimeSpan.FromTicks(1)),
        TimeSpan.FromSeconds(61)
    };

    [Fact]
    public void Options_and_builder_expose_exact_defaults()
    {
        var options = new TSqlDurableOutputStoreOptions();
        var builder = new TSqlDurableOutputStoreOptionsBuilder();

        options.ConnectionString.ShouldBeNull();
        options.CommandTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.SchemaLockTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        options.ConnectRetryCount.ShouldBe(1);
        options.ConnectRetryInterval.ShouldBe(TimeSpan.FromSeconds(1));
        options.SchemaManagement.ShouldBe(TSqlDurableOutputSchemaManagement.CreateOrMigrate);
        builder.ConnectionString.ShouldBeNull();
        builder.CommandTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        builder.SchemaLockTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        builder.ConnectRetryCount.ShouldBe(1);
        builder.ConnectRetryInterval.ShouldBe(TimeSpan.FromSeconds(1));
        builder.SchemaManagement.ShouldBe(TSqlDurableOutputSchemaManagement.CreateOrMigrate);
    }

    [Theory]
    [MemberData(nameof(ValidCommandTimeouts))]
    public void Command_timeout_accepts_inclusive_whole_second_boundaries(TimeSpan value)
        => new TSqlDurableOutputStoreOptions { CommandTimeout = value }
            .CommandTimeout.ShouldBe(value);

    [Theory]
    [MemberData(nameof(InvalidCommandTimeouts))]
    public void Command_timeout_rejects_out_of_range_or_fractional_seconds(TimeSpan value)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            new TSqlDurableOutputStoreOptions { CommandTimeout = value });

        exception.ParamName.ShouldBe(nameof(TSqlDurableOutputStoreOptions.CommandTimeout));
        exception.Message.ShouldContain("whole seconds");
    }

    [Theory]
    [MemberData(nameof(ValidSchemaLockTimeouts))]
    public void Schema_lock_timeout_accepts_zero_and_inclusive_whole_millisecond_boundaries(
        TimeSpan value)
        => new TSqlDurableOutputStoreOptions { SchemaLockTimeout = value }
            .SchemaLockTimeout.ShouldBe(value);

    [Theory]
    [MemberData(nameof(InvalidSchemaLockTimeouts))]
    public void Schema_lock_timeout_rejects_out_of_range_or_fractional_milliseconds(
        TimeSpan value)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            new TSqlDurableOutputStoreOptions { SchemaLockTimeout = value });

        exception.ParamName.ShouldBe(nameof(TSqlDurableOutputStoreOptions.SchemaLockTimeout));
        exception.Message.ShouldContain("whole milliseconds");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Connect_retry_count_accepts_inclusive_boundaries(int value)
        => new TSqlDurableOutputStoreOptions { ConnectRetryCount = value }
            .ConnectRetryCount.ShouldBe(value);

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void Connect_retry_count_rejects_values_outside_inclusive_range(int value)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            new TSqlDurableOutputStoreOptions { ConnectRetryCount = value });

        exception.ParamName.ShouldBe(nameof(TSqlDurableOutputStoreOptions.ConnectRetryCount));
        exception.Message.ShouldContain("between zero and five");
    }

    [Theory]
    [MemberData(nameof(ValidConnectRetryIntervals))]
    public void Connect_retry_interval_accepts_inclusive_whole_second_boundaries(TimeSpan value)
        => new TSqlDurableOutputStoreOptions { ConnectRetryInterval = value }
            .ConnectRetryInterval.ShouldBe(value);

    [Theory]
    [MemberData(nameof(InvalidConnectRetryIntervals))]
    public void Connect_retry_interval_rejects_out_of_range_or_fractional_seconds(TimeSpan value)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            new TSqlDurableOutputStoreOptions { ConnectRetryInterval = value });

        exception.ParamName.ShouldBe(nameof(TSqlDurableOutputStoreOptions.ConnectRetryInterval));
        exception.Message.ShouldContain("whole seconds");
    }

    [Theory]
    [InlineData(TSqlDurableOutputSchemaManagement.CreateOrMigrate)]
    [InlineData(TSqlDurableOutputSchemaManagement.ValidateOnly)]
    public void Schema_management_accepts_every_defined_value(
        TSqlDurableOutputSchemaManagement value)
        => new TSqlDurableOutputStoreOptions { SchemaManagement = value }
            .SchemaManagement.ShouldBe(value);

    [Fact]
    public void Schema_management_rejects_undefined_values()
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            new TSqlDurableOutputStoreOptions
            {
                SchemaManagement = (TSqlDurableOutputSchemaManagement)int.MaxValue
            });

        exception.ParamName.ShouldBe(nameof(TSqlDurableOutputStoreOptions.SchemaManagement));
        exception.Message.ShouldContain("mode is invalid");
    }

    [Fact]
    public void Resolve_normalizes_connection_and_overrides_only_retry_settings()
    {
        const string raw =
            " Server=database.example.test;Database=FluxFlow;Application Name=Host App;" +
            "Encrypt=False;Connect Timeout=17;Connect Retry Count=5;Connect Retry Interval=60 ";
        var options = new TSqlDurableOutputStoreOptions
        {
            ConnectionString = raw,
            CommandTimeout = TimeSpan.FromSeconds(9),
            SchemaLockTimeout = TimeSpan.FromMilliseconds(1250),
            ConnectRetryCount = 2,
            ConnectRetryInterval = TimeSpan.FromSeconds(7),
            SchemaManagement = TSqlDurableOutputSchemaManagement.ValidateOnly
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
        settings.SchemaManagement.ShouldBe(TSqlDurableOutputSchemaManagement.ValidateOnly);
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
        var options = new TSqlDurableOutputStoreOptions { ConnectionString = connectionString };

        var exception = Should.Throw(
            () => new TSqlDurableOutputStore(options),
            exceptionType);

        exception.Message.ShouldContain(expectedMessage);
    }

    [Fact]
    public void Resolve_rejects_malformed_connection_without_revealing_secret_or_opening()
    {
        const string secret = "Sensitive-Password-Sentinel";
        var options = new TSqlDurableOutputStoreOptions
        {
            ConnectionString =
                $"Server=database.example.test;Database=FluxFlow;Password={secret};Unknown Keyword=value"
        };

        var exception = Should.Throw<ArgumentException>(() => new TSqlDurableOutputStore(options));

        exception.ParamName.ShouldBe(nameof(TSqlDurableOutputStoreOptions.ConnectionString));
        exception.Message.ShouldBe("T-SQL durable output connection string is invalid. (Parameter 'ConnectionString')");
        exception.ToString().ShouldNotContain(secret, Case.Insensitive);
    }

    [Fact]
    public void Builder_build_snapshots_every_value_into_immutable_options()
    {
        var builder = new TSqlDurableOutputStoreOptionsBuilder
        {
            ConnectionString = " Server=database.example.test;Database=FluxFlow;Encrypt=False ",
            CommandTimeout = TimeSpan.FromSeconds(8),
            SchemaLockTimeout = TimeSpan.FromMilliseconds(3210),
            ConnectRetryCount = 4,
            ConnectRetryInterval = TimeSpan.FromSeconds(12),
            SchemaManagement = TSqlDurableOutputSchemaManagement.ValidateOnly
        };

        var options = builder.Build();
        builder.ConnectionString = TSqlDurableOutputTestData.UnreachableConnectionString;
        builder.CommandTimeout = TimeSpan.FromSeconds(1);
        builder.SchemaLockTimeout = TimeSpan.Zero;
        builder.ConnectRetryCount = 0;
        builder.ConnectRetryInterval = TimeSpan.FromSeconds(1);
        builder.SchemaManagement = TSqlDurableOutputSchemaManagement.CreateOrMigrate;

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
        options.SchemaManagement.ShouldBe(TSqlDurableOutputSchemaManagement.ValidateOnly);
    }

    [Fact]
    public void Options_string_representation_does_not_expose_connection_secret()
    {
        const string secret = "Options-Secret-Sentinel";
        var options = new TSqlDurableOutputStoreOptions
        {
            ConnectionString =
                $"Server=database.example.test;Database=FluxFlow;User ID=host;Password={secret}"
        };

        options.ToString().ShouldNotContain(secret, Case.Insensitive);
        options.ToString().ShouldNotContain("Password", Case.Insensitive);
    }
}
