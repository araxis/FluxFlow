using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.SqlFile.Tests;

public sealed class SqlFileDurableOutputRegistrationTests
{
    [Fact]
    public void Registration_returns_original_collection_invokes_configuration_once_and_snapshots_every_value()
    {
        var services = new ServiceCollection();
        var configureCalls = 0;
        SqlFileDurableOutputStoreOptionsBuilder? capturedBuilder = null;

        var returned = services.AddFluxFlowSqlFileDurableOutput(builder =>
        {
            configureCalls++;
            capturedBuilder = builder;
            builder.DatabasePath = " durable-output.db ";
            builder.CreateDatabase = false;
            builder.CreateDirectory = false;
            builder.AllowAbsoluteDatabasePath = false;
            builder.BusyTimeout = TimeSpan.FromSeconds(7);
        });
        capturedBuilder!.DatabasePath = "mutated.db";
        capturedBuilder.CreateDatabase = true;
        capturedBuilder.CreateDirectory = true;
        capturedBuilder.AllowAbsoluteDatabasePath = true;
        capturedBuilder.BusyTimeout = TimeSpan.FromSeconds(99);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<SqlFileDurableOutputStoreOptions>();

        returned.ShouldBeSameAs(services);
        configureCalls.ShouldBe(1);
        options.DatabasePath.ShouldBe("durable-output.db");
        options.CreateDatabase.ShouldBeFalse();
        options.CreateDirectory.ShouldBeFalse();
        options.AllowAbsoluteDatabasePath.ShouldBeFalse();
        options.BusyTimeout.ShouldBe(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void Equivalent_registration_is_idempotent_without_duplicate_descriptors()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowSqlFileDurableOutput(builder =>
            builder.DatabasePath = " durable-output.db ");
        var descriptorCount = services.Count;

        var returned = services.AddFluxFlowSqlFileDurableOutput(builder =>
            builder.DatabasePath = "durable-output.db");

        returned.ShouldBeSameAs(services);
        services.Count.ShouldBe(descriptorCount);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableOutputStoreOptions)).ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableOutputStore)).ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IDurableOutputStore)).ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IDurableOutputDeliveryStore)).ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IDurableOutputDeadLetterStore)).ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IDurableOutputStatusStore)).ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IDurableOutputRetentionStore)).ShouldBe(1);
    }

    [Fact]
    public void Different_repeated_registration_fails_without_partial_descriptors()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowSqlFileDurableOutput(builder => builder.DatabasePath = "first.db");
        var descriptors = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableOutput(builder =>
                builder.DatabasePath = "second.db"));

        exception.Message.ShouldContain("different options");
        services.ShouldBe(descriptors);
    }

    [Fact]
    public void Preexisting_store_registration_conflicts_atomically()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDurableOutputStore>(new StubDurableOutputStore());
        var descriptors = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableOutput(builder =>
                builder.DatabasePath = "durable-output.db"));

        exception.Message.ShouldContain(nameof(IDurableOutputStore));
        services.ShouldBe(descriptors);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableOutputStore));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableOutputStoreOptions));
    }

    [Fact]
    public void Preexisting_delivery_store_registration_conflicts_atomically()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDurableOutputDeliveryStore>(new StubDurableOutputDeliveryStore());
        var descriptors = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableOutput(builder =>
                builder.DatabasePath = "durable-output.db"));

        exception.Message.ShouldContain(nameof(IDurableOutputDeliveryStore));
        services.ShouldBe(descriptors);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableOutputStore));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableOutputStoreOptions));
    }

    [Fact]
    public void Preexisting_dead_letter_store_registration_conflicts_atomically()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDurableOutputDeadLetterStore>(new StubDurableOutputDeadLetterStore());
        var descriptors = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableOutput(builder =>
                builder.DatabasePath = "durable-output.db"));

        exception.Message.ShouldContain(nameof(IDurableOutputDeadLetterStore));
        services.ShouldBe(descriptors);
    }

    [Fact]
    public void Preexisting_status_registration_conflicts_atomically()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDurableOutputStatusStore>(new StubDurableOutputStatusStore());
        var descriptors = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableOutput(builder =>
                builder.DatabasePath = "durable-output.db"));

        exception.Message.ShouldContain(nameof(IDurableOutputStatusStore));
        services.ShouldBe(descriptors);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableOutputStore));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableOutputStoreOptions));
    }

    [Fact]
    public void Preexisting_retention_registration_conflicts_atomically()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDurableOutputRetentionStore>(new StubDurableOutputRetentionStore());
        var descriptors = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableOutput(builder =>
                builder.DatabasePath = "durable-output.db"));

        exception.Message.ShouldContain(nameof(IDurableOutputRetentionStore));
        services.ShouldBe(descriptors);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableOutputStore));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableOutputStoreOptions));
    }

    [Fact]
    public void Tampered_repeated_registration_fails_without_repairing_or_adding_descriptors()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowSqlFileDurableOutput(builder =>
            builder.DatabasePath = "durable-output.db");
        var alias = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IDurableOutputStore));
        services.Remove(alias);
        var descriptors = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableOutput(builder =>
                builder.DatabasePath = "durable-output.db"));

        exception.Message.ShouldContain("service ownership");
        services.ShouldBe(descriptors);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IDurableOutputStore));
    }

    [Fact]
    public void Tampered_dead_letter_alias_fails_without_repair_or_partial_descriptors()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowSqlFileDurableOutput(builder =>
            builder.DatabasePath = "durable-output.db");
        var alias = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IDurableOutputDeadLetterStore));
        services.Remove(alias);
        var descriptors = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableOutput(builder =>
                builder.DatabasePath = "durable-output.db"));

        exception.Message.ShouldContain("service ownership");
        services.ShouldBe(descriptors);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IDurableOutputDeadLetterStore));
    }

    [Fact]
    public void Tampered_status_alias_fails_without_repair_or_partial_descriptors()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowSqlFileDurableOutput(builder =>
            builder.DatabasePath = "durable-output.db");
        var alias = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IDurableOutputStatusStore));
        services.Remove(alias);
        var descriptors = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableOutput(builder =>
                builder.DatabasePath = "durable-output.db"));

        exception.Message.ShouldContain("service ownership");
        services.ShouldBe(descriptors);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IDurableOutputStatusStore));
    }

    [Fact]
    public void Tampered_retention_alias_fails_without_repair_or_partial_descriptors()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowSqlFileDurableOutput(builder =>
            builder.DatabasePath = "durable-output.db");
        var alias = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IDurableOutputRetentionStore));
        services.Remove(alias);
        var descriptors = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableOutput(builder =>
                builder.DatabasePath = "durable-output.db"));

        exception.Message.ShouldContain("service ownership");
        services.ShouldBe(descriptors);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IDurableOutputRetentionStore));
    }

    [Fact]
    public async Task Concrete_capture_delivery_and_dead_letter_aliases_share_one_singleton()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowSqlFileDurableOutput(builder =>
            builder.DatabasePath = "durable-output.db");
        await using var provider = services.BuildServiceProvider();

        var concrete = provider.GetRequiredService<SqlFileDurableOutputStore>();
        var capture = provider.GetRequiredService<IDurableOutputStore>();
        var delivery = provider.GetRequiredService<IDurableOutputDeliveryStore>();
        var deadLetters = provider.GetRequiredService<IDurableOutputDeadLetterStore>();
        var status = provider.GetRequiredService<IDurableOutputStatusStore>();
        var retention = provider.GetRequiredService<IDurableOutputRetentionStore>();
        var secondConcrete = provider.GetRequiredService<SqlFileDurableOutputStore>();

        capture.ShouldBeSameAs(concrete);
        delivery.ShouldBeSameAs(concrete);
        deadLetters.ShouldBeSameAs(concrete);
        status.ShouldBeSameAs(concrete);
        retention.ShouldBeSameAs(concrete);
        secondConcrete.ShouldBeSameAs(concrete);
        concrete.ShouldBeAssignableTo<IAsyncDisposable>();
    }

    [Fact]
    public async Task Registration_validates_but_performs_no_directory_or_database_io()
    {
        using var database = TemporarySqliteDatabase.Create();
        var nestedDirectory = Path.Combine(database.DirectoryPath, "not-created");
        var path = Path.Combine(nestedDirectory, "durable-output.db");
        var services = new ServiceCollection();

        services.AddFluxFlowSqlFileDurableOutput(builder =>
        {
            builder.DatabasePath = path;
            builder.AllowAbsoluteDatabasePath = true;
        });

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        provider.GetRequiredService<IDurableOutputRetentionStore>()
            .ShouldBeSameAs(provider.GetRequiredService<SqlFileDurableOutputStore>());

        Directory.Exists(nestedDirectory).ShouldBeFalse();
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public void Registration_rejects_null_inputs_and_missing_path_before_mutation()
    {
        var services = new ServiceCollection();
        var nullServices = Should.Throw<ArgumentNullException>(() =>
            SqlFileDurableOutputServiceCollectionExtensions.AddFluxFlowSqlFileDurableOutput(
                null!,
                _ => { }));
        var nullConfigure = Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowSqlFileDurableOutput(null!));
        var missingPath = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableOutput(_ => { }));

        nullServices.ParamName.ShouldBe("services");
        nullConfigure.ParamName.ShouldBe("configure");
        missingPath.Message.ShouldContain("database path");
        services.ShouldBeEmpty();
    }

    [Fact]
    public void Invalid_builder_timeout_fails_before_adding_any_descriptors()
    {
        var services = new ServiceCollection();

        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddFluxFlowSqlFileDurableOutput(builder =>
            {
                builder.DatabasePath = "durable-output.db";
                builder.BusyTimeout = TimeSpan.Zero;
            }));

        exception.ParamName.ShouldBe(nameof(SqlFileDurableOutputStoreOptions.BusyTimeout));
        services.ShouldBeEmpty();
    }

    private sealed class StubDurableOutputStore : IDurableOutputStore
    {
        public ValueTask<DurableOutputEnqueueResult> EnqueueAsync(
            DurableOutputEnvelope envelope,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubDurableOutputDeliveryStore : IDurableOutputDeliveryStore
    {
        public ValueTask<DurableOutputDeliveryLease?> TryLeaseAsync(
            DurableOutputDeliveryLeaseRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<DurableOutputDeliveryLease?>(null);

        public ValueTask<DurableOutputDeliveryTransitionResult> RenewLeaseAsync(
            DurableOutputDeliveryLeaseRenewal renewal,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<DurableOutputDeliveryTransitionResult> CompleteAsync(
            DurableOutputDeliveryTransition transition,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<DurableOutputDeliveryTransitionResult> RetryAsync(
            DurableOutputDeliveryRetry retry,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<DurableOutputDeliveryTransitionResult> DeadLetterAsync(
            DurableOutputDeliveryDeadLetter deadLetter,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubDurableOutputDeadLetterStore : IDurableOutputDeadLetterStore
    {
        public ValueTask<DurableOutputDeadLetterPage> ListAsync(
            DurableOutputDeadLetterQuery query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<DurableOutputDeadLetterDetails?> GetAsync(
            DurableOutputKey key,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<DurableOutputReplayResult> ReplayAsync(
            DurableOutputReplay replay,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubDurableOutputStatusStore : IDurableOutputStatusStore
    {
        public ValueTask<DurableOutputStatusSnapshot> GetStatusAsync(
            DurableOutputStatusQuery query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubDurableOutputRetentionStore : IDurableOutputRetentionStore
    {
        public ValueTask<DurableOutputRetentionResult> PurgeCompletedAsync(
            DurableOutputRetentionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<DurableOutputRetentionResult> PurgeDeadLettersAsync(
            DurableOutputRetentionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
