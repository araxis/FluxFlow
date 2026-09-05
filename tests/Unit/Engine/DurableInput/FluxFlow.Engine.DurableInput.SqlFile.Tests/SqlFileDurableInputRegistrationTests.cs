using FluxFlow.Engine.DurableInput.Tests;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.SqlFile.Tests;

public sealed class SqlFileDurableInputRegistrationTests
{
    [Fact]
    public void Registration_returns_the_original_collection_invokes_configuration_once_and_projects_every_value()
    {
        var services = new ServiceCollection();
        var configureCalls = 0;
        SqlFileDurableInputStoreOptionsBuilder? capturedBuilder = null;

        var returned = services.AddFluxFlowSqlFileDurableInput(builder =>
        {
            configureCalls++;
            capturedBuilder = builder;
            builder.DatabasePath = " durable-input.db ";
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
        var options = provider.GetRequiredService<SqlFileDurableInputStoreOptions>();

        returned.ShouldBeSameAs(services);
        configureCalls.ShouldBe(1);
        options.DatabasePath.ShouldBe("durable-input.db");
        options.CreateDatabase.ShouldBeFalse();
        options.CreateDirectory.ShouldBeFalse();
        options.AllowAbsoluteDatabasePath.ShouldBeFalse();
        options.BusyTimeout.ShouldBe(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void Equivalent_registration_is_idempotent_without_invoking_prior_configuration_again()
    {
        var services = new ServiceCollection();
        var firstCalls = 0;
        var secondCalls = 0;
        services.AddFluxFlowSqlFileDurableInput(builder =>
        {
            firstCalls++;
            builder.DatabasePath = " durable-input.db ";
        });
        var descriptorCount = services.Count;

        var returned = services.AddFluxFlowSqlFileDurableInput(builder =>
        {
            secondCalls++;
            builder.DatabasePath = "durable-input.db";
        });

        returned.ShouldBeSameAs(services);
        firstCalls.ShouldBe(1);
        secondCalls.ShouldBe(1);
        services.Count.ShouldBe(descriptorCount);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableInputStoreOptions)).ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableInputStore)).ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IDurableInputStore)).ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IDurableInputDeadLetterStore)).ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IDurableInputLeaseRenewalStore)).ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IDurableInputStatusStore)).ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IDurableInputRetentionStore)).ShouldBe(1);
    }

    [Fact]
    public void Different_repeated_registration_fails_without_partial_descriptors()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowSqlFileDurableInput(builder =>
        {
            builder.DatabasePath = "first.db";
        });
        var descriptorCount = services.Count;

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableInput(builder =>
            {
                builder.DatabasePath = "second.db";
            }));

        exception.Message.ShouldContain("different options");
        services.Count.ShouldBe(descriptorCount);
    }

    [Fact]
    public void Any_preexisting_store_interface_registration_conflicts_without_partial_descriptors()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDurableInputStore>(new StubDurableInputStore());
        var descriptorCount = services.Count;

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableInput(builder =>
            {
                builder.DatabasePath = "durable-input.db";
            }));

        exception.Message.ShouldContain(nameof(IDurableInputStore));
        services.Count.ShouldBe(descriptorCount);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableInputStore));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableInputStoreOptions));
    }

    [Fact]
    public void Any_preexisting_dead_letter_interface_registration_conflicts_without_partial_descriptors()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDurableInputDeadLetterStore>(new StubDeadLetterStore());
        var descriptorCount = services.Count;

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableInput(builder =>
            {
                builder.DatabasePath = "durable-input.db";
            }));

        exception.Message.ShouldContain(nameof(IDurableInputDeadLetterStore));
        services.Count.ShouldBe(descriptorCount);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableInputStore));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableInputStoreOptions));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IDurableInputStore));
    }

    [Fact]
    public void Any_preexisting_renewal_interface_registration_conflicts_without_partial_descriptors()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDurableInputLeaseRenewalStore>(new StubLeaseRenewalStore());
        var descriptorCount = services.Count;

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableInput(builder =>
            {
                builder.DatabasePath = "durable-input.db";
            }));

        exception.Message.ShouldContain(nameof(IDurableInputLeaseRenewalStore));
        services.Count.ShouldBe(descriptorCount);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableInputStore));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableInputStoreOptions));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IDurableInputStore));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IDurableInputDeadLetterStore));
    }

    [Fact]
    public void Preexisting_status_registration_conflicts_without_partial_descriptors()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDurableInputStatusStore>(new StubStatusStore());
        var descriptors = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableInput(builder =>
                builder.DatabasePath = "durable-input.db"));

        exception.Message.ShouldContain(nameof(IDurableInputStatusStore));
        services.ShouldBe(descriptors);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableInputStore));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableInputStoreOptions));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IDurableInputStore));
    }

    [Fact]
    public void Preexisting_retention_registration_conflicts_without_partial_descriptors()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDurableInputRetentionStore>(new StubRetentionStore());
        var descriptors = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableInput(builder =>
                builder.DatabasePath = "durable-input.db"));

        exception.Message.ShouldContain(nameof(IDurableInputRetentionStore));
        services.ShouldBe(descriptors);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableInputStore));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(SqlFileDurableInputStoreOptions));
    }

    [Fact]
    public void Tampered_status_alias_fails_without_repair_or_partial_descriptors()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowSqlFileDurableInput(builder =>
            builder.DatabasePath = "durable-input.db");
        var alias = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IDurableInputStatusStore));
        services.Remove(alias);
        var descriptors = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableInput(builder =>
                builder.DatabasePath = "durable-input.db"));

        exception.Message.ShouldContain("service ownership");
        services.ShouldBe(descriptors);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IDurableInputStatusStore));
    }

    [Fact]
    public void Tampered_retention_alias_fails_without_repair_or_partial_descriptors()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowSqlFileDurableInput(builder =>
            builder.DatabasePath = "durable-input.db");
        var alias = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IDurableInputRetentionStore));
        services.Remove(alias);
        var descriptors = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowSqlFileDurableInput(builder =>
                builder.DatabasePath = "durable-input.db"));

        exception.Message.ShouldContain("service ownership");
        services.ShouldBe(descriptors);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(IDurableInputRetentionStore));
    }

    [Fact]
    public async Task Concrete_store_and_interface_alias_are_the_same_singleton_instance()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowSqlFileDurableInput(builder =>
        {
            builder.DatabasePath = "durable-input.db";
        });
        await using var provider = services.BuildServiceProvider();

        var concrete = provider.GetRequiredService<SqlFileDurableInputStore>();
        var contract = provider.GetRequiredService<IDurableInputStore>();
        var deadLetters = provider.GetRequiredService<IDurableInputDeadLetterStore>();
        var renewal = provider.GetRequiredService<IDurableInputLeaseRenewalStore>();
        var status = provider.GetRequiredService<IDurableInputStatusStore>();
        var retention = provider.GetRequiredService<IDurableInputRetentionStore>();
        var secondConcrete = provider.GetRequiredService<SqlFileDurableInputStore>();

        contract.ShouldBeSameAs(concrete);
        deadLetters.ShouldBeSameAs(concrete);
        deadLetters.ShouldBeSameAs(contract);
        renewal.ShouldBeSameAs(concrete);
        renewal.ShouldBeSameAs(contract);
        status.ShouldBeSameAs(concrete);
        status.ShouldBeSameAs(contract);
        retention.ShouldBeSameAs(concrete);
        retention.ShouldBeSameAs(contract);
        secondConcrete.ShouldBeSameAs(concrete);
        concrete.ShouldBeAssignableTo<IAsyncDisposable>();
    }

    [Fact]
    public async Task Registration_validates_but_performs_no_directory_or_database_io()
    {
        using var database = TemporarySqliteDatabase.Create();
        var nestedDirectory = Path.Combine(database.DirectoryPath, "not-created");
        var path = Path.Combine(nestedDirectory, "durable-input.db");
        var services = new ServiceCollection();

        services.AddFluxFlowSqlFileDurableInput(builder =>
        {
            builder.DatabasePath = path;
            builder.AllowAbsoluteDatabasePath = true;
            builder.CreateDatabase = true;
            builder.CreateDirectory = true;
        });

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        provider.GetRequiredService<IDurableInputRetentionStore>()
            .ShouldBeSameAs(provider.GetRequiredService<SqlFileDurableInputStore>());

        Directory.Exists(nestedDirectory).ShouldBeFalse();
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public void Registration_rejects_null_services_and_an_omitted_required_path()
    {
        var nullException = Should.Throw<ArgumentNullException>(() =>
            SqlFileDurableInputServiceCollectionExtensions.AddFluxFlowSqlFileDurableInput(null!));
        var pathException = Should.Throw<InvalidOperationException>(() =>
            new ServiceCollection().AddFluxFlowSqlFileDurableInput());

        nullException.ParamName.ShouldBe("services");
        pathException.Message.ShouldContain("database path");
    }

    [Fact]
    public void Invalid_builder_timeout_fails_before_adding_any_descriptors()
    {
        var services = new ServiceCollection();

        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddFluxFlowSqlFileDurableInput(builder =>
            {
                builder.DatabasePath = "durable-input.db";
                builder.BusyTimeout = TimeSpan.Zero;
            }));

        exception.ParamName.ShouldBe(nameof(SqlFileDurableInputStoreOptions.BusyTimeout));
        services.ShouldBeEmpty();
    }

    private sealed class StubDurableInputStore : IDurableInputStore
    {
        public ValueTask<DurableInputEnqueueResult> EnqueueAsync(
            DurableInputEnvelope envelope,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<DurableInputLease>> LeaseAsync(
            DurableInputLeaseRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<DurableInputTransitionResult> MarkDeliveredAsync(
            DurableInputLeaseTransition transition,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<DurableInputTransitionResult> ReleaseAsync(
            DurableInputRelease release,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<DurableInputTransitionResult> DeadLetterAsync(
            DurableInputDeadLetter deadLetter,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubDeadLetterStore : IDurableInputDeadLetterStore
    {
        public ValueTask<DurableInputDeadLetterPage> ListAsync(
            DurableInputDeadLetterQuery query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<DurableInputDeadLetterDetails?> GetAsync(
            DurableInputKey key,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<DurableInputReplayResult> ReplayAsync(
            DurableInputReplay replay,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubLeaseRenewalStore : IDurableInputLeaseRenewalStore
    {
        public ValueTask<DurableInputTransitionResult> RenewLeaseAsync(
            DurableInputLeaseRenewal renewal,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubStatusStore : IDurableInputStatusStore
    {
        public ValueTask<DurableInputStatusSnapshot> GetStatusAsync(
            DurableInputStatusQuery query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubRetentionStore : IDurableInputRetentionStore
    {
        public ValueTask<DurableInputRetentionResult> PurgeDeliveredAsync(
            DurableInputRetentionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<DurableInputRetentionResult> PurgeDeadLettersAsync(
            DurableInputRetentionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
