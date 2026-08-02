using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputStoreOptionsTests
{
    [Fact]
    public void Options_and_builder_expose_the_exact_conservative_defaults()
    {
        var options = new SqlFileDurableOutputStoreOptions();
        var builder = new SqlFileDurableOutputStoreOptionsBuilder();

        options.DatabasePath.ShouldBeNull();
        options.CreateDatabase.ShouldBeTrue();
        options.CreateDirectory.ShouldBeTrue();
        options.AllowAbsoluteDatabasePath.ShouldBeFalse();
        options.BusyTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        builder.DatabasePath.ShouldBeNull();
        builder.CreateDatabase.ShouldBeTrue();
        builder.CreateDirectory.ShouldBeTrue();
        builder.AllowAbsoluteDatabasePath.ShouldBeFalse();
        builder.BusyTimeout.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Options_trim_relative_database_path_without_touching_the_file_system()
    {
        var relativePath = $" durable-output-{Guid.NewGuid():N}.db ";
        var options = new SqlFileDurableOutputStoreOptions { DatabasePath = relativePath };

        await using var store = new SqlFileDurableOutputStore(options);

        options.DatabasePath.ShouldBe(relativePath.Trim());
        File.Exists(Path.GetFullPath(relativePath.Trim())).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Store_rejects_missing_or_blank_database_path(string? databasePath)
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            new SqlFileDurableOutputStore(new SqlFileDurableOutputStoreOptions
            {
                DatabasePath = databasePath
            }));

        exception.Message.ShouldContain("database path");
    }

    [Fact]
    public void Store_rejects_an_absolute_path_when_absolute_paths_are_disabled()
    {
        using var database = TemporarySqliteDatabase.Create();

        var exception = Should.Throw<InvalidOperationException>(() =>
            new SqlFileDurableOutputStore(new SqlFileDurableOutputStoreOptions
            {
                DatabasePath = database.DatabasePath
            }));

        exception.Message.ShouldContain("absolute");
        File.Exists(database.DatabasePath).ShouldBeFalse();
    }

    [Fact]
    public async Task Busy_timeout_accepts_the_exact_positive_and_maximum_boundaries()
    {
        var minimum = new SqlFileDurableOutputStoreOptions
        {
            DatabasePath = "minimum.db",
            BusyTimeout = TimeSpan.FromTicks(1)
        };
        var maximum = new SqlFileDurableOutputStoreOptions
        {
            DatabasePath = "maximum.db",
            BusyTimeout = TimeSpan.FromMilliseconds(int.MaxValue)
        };

        await using var minimumStore = new SqlFileDurableOutputStore(minimum);
        await using var maximumStore = new SqlFileDurableOutputStore(maximum);

        minimum.BusyTimeout.ShouldBe(TimeSpan.FromTicks(1));
        maximum.BusyTimeout.ShouldBe(TimeSpan.FromMilliseconds(int.MaxValue));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2147483648d)]
    public void Busy_timeout_rejects_zero_negative_and_above_sqlite_maximum(double milliseconds)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            new SqlFileDurableOutputStoreOptions
            {
                DatabasePath = "invalid.db",
                BusyTimeout = TimeSpan.FromMilliseconds(milliseconds)
            });

        exception.ParamName.ShouldBe(nameof(SqlFileDurableOutputStoreOptions.BusyTimeout));
        exception.Message.ShouldContain("greater than zero");
        exception.Message.ShouldContain(int.MaxValue.ToString());
    }

    [Fact]
    public void Store_rejects_null_options()
    {
        var exception = Should.Throw<ArgumentNullException>(() =>
            new SqlFileDurableOutputStore(null!));

        exception.ParamName.ShouldBe("options");
    }
}
