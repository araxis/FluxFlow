using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.TSql.Tests;

public sealed class TSqlDurableOutputRegistrationTests
{
    [Fact]
    public void Registration_returns_original_collection_invokes_callback_once_and_snapshots_options()
    {
        var services = new ServiceCollection();
        var calls = 0;
        TSqlDurableOutputStoreOptionsBuilder? captured = null;

        var returned = services.AddFluxFlowTSqlDurableOutput(builder =>
        {
            calls++;
            captured = builder;
            builder.ConnectionString =
                " Server=database.example.test;Database=FluxFlow;Encrypt=False ";
            builder.CommandTimeout = TimeSpan.FromSeconds(8);
            builder.SchemaLockTimeout = TimeSpan.FromMilliseconds(1234);
            builder.ConnectRetryCount = 3;
            builder.ConnectRetryInterval = TimeSpan.FromSeconds(9);
            builder.SchemaManagement = TSqlDurableOutputSchemaManagement.ValidateOnly;
        });

        captured.ShouldNotBeNull();
        captured.ConnectionString = TSqlDurableOutputTestData.UnreachableConnectionString;
        captured.CommandTimeout = TimeSpan.FromSeconds(1);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<TSqlDurableOutputStoreOptions>();

        returned.ShouldBeSameAs(services);
        calls.ShouldBe(1);
        var normalized = new SqlConnectionStringBuilder(options.ConnectionString);
        normalized.DataSource.ShouldBe("database.example.test");
        normalized.InitialCatalog.ShouldBe("FluxFlow");
        normalized["Encrypt"].ToString().ShouldBe("False");
        normalized.ConnectRetryCount.ShouldBe(3);
        normalized.ConnectRetryInterval.ShouldBe(9);
        options.CommandTimeout.ShouldBe(TimeSpan.FromSeconds(8));
        options.SchemaLockTimeout.ShouldBe(TimeSpan.FromMilliseconds(1234));
        options.ConnectRetryCount.ShouldBe(3);
        options.ConnectRetryInterval.ShouldBe(TimeSpan.FromSeconds(9));
        options.SchemaManagement.ShouldBe(TSqlDurableOutputSchemaManagement.ValidateOnly);
    }

    [Fact]
    public void Registration_rejects_null_inputs_before_mutation()
    {
        var services = new ServiceCollection();

        var nullServices = Should.Throw<ArgumentNullException>(() =>
            TSqlDurableOutputServiceCollectionExtensions.AddFluxFlowTSqlDurableOutput(
                null!,
                _ => { }));
        var nullConfigure = Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowTSqlDurableOutput(null!));

        nullServices.ParamName.ShouldBe("services");
        nullConfigure.ParamName.ShouldBe("configure");
        services.ShouldBeEmpty();
    }

    [Fact]
    public void Callback_failure_leaves_collection_exactly_unchanged()
    {
        var marker = new object();
        var services = new ServiceCollection();
        services.AddSingleton(marker);
        var before = services.ToArray();

        var exception = Should.Throw<IntentionalConfigurationException>(() =>
            services.AddFluxFlowTSqlDurableOutput(_ =>
                throw new IntentionalConfigurationException("configuration failed")));

        exception.Message.ShouldBe("configuration failed");
        services.ShouldBe(before);
        services.Single().ImplementationInstance.ShouldBeSameAs(marker);
    }

    [Fact]
    public void Invalid_configuration_leaves_collection_exactly_unchanged()
    {
        var marker = new object();
        var services = new ServiceCollection();
        services.AddSingleton(marker);
        var before = services.ToArray();

        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddFluxFlowTSqlDurableOutput(builder =>
            {
                builder.ConnectionString = TSqlDurableOutputTestData.UnreachableConnectionString;
                builder.CommandTimeout = TimeSpan.Zero;
            }));

        exception.ParamName.ShouldBe(nameof(TSqlDurableOutputStoreOptions.CommandTimeout));
        services.ShouldBe(before);
        services.Single().ImplementationInstance.ShouldBeSameAs(marker);
    }

    [Fact]
    public void Semantically_equivalent_normalized_registration_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowTSqlDurableOutput(builder =>
        {
            builder.ConnectionString =
                " Server=database.example.test;Database=FluxFlow;Encrypt=False;Application Name=Host ";
            builder.ConnectRetryCount = 2;
            builder.ConnectRetryInterval = TimeSpan.FromSeconds(7);
        });
        var before = services.ToArray();

        var returned = services.AddFluxFlowTSqlDurableOutput(builder =>
        {
            builder.ConnectionString =
                "Application Name=Host;Initial Catalog=FluxFlow;Data Source=database.example.test;Encrypt=False";
            builder.ConnectRetryCount = 2;
            builder.ConnectRetryInterval = TimeSpan.FromSeconds(7);
        });

        returned.ShouldBeSameAs(services);
        services.ShouldBe(before);
        AssertExactProviderShape(services);
    }

    [Fact]
    public void Conflicting_repeat_fails_without_changing_any_descriptor()
    {
        var services = ValidServices();
        var before = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowTSqlDurableOutput(builder =>
            {
                builder.ConnectionString = TSqlDurableOutputTestData.UnreachableConnectionString;
                builder.CommandTimeout = TimeSpan.FromSeconds(31);
            }));

        exception.Message.ShouldContain("different options");
        services.ShouldBe(before);
    }

    [Theory]
    [InlineData(typeof(IDurableOutputStore))]
    [InlineData(typeof(IDurableOutputDeliveryStore))]
    [InlineData(typeof(IDurableOutputDeadLetterStore))]
    [InlineData(typeof(IDurableOutputStatusStore))]
    [InlineData(typeof(IDurableOutputRetentionStore))]
    public void Preexisting_interface_registration_conflicts_atomically(Type contract)
    {
        var marker = new object();
        var services = new ServiceCollection();
        ((ICollection<ServiceDescriptor>)services).Add(
            ServiceDescriptor.Singleton(contract, _ => marker));
        var before = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowTSqlDurableOutput(builder =>
                builder.ConnectionString = TSqlDurableOutputTestData.UnreachableConnectionString));

        exception.Message.ShouldContain(contract.Name);
        services.ShouldBe(before);
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(TSqlDurableOutputStore));
        services.ShouldNotContain(descriptor =>
            descriptor.ServiceType == typeof(TSqlDurableOutputStoreOptions));
    }

    [Theory]
    [InlineData(typeof(IDurableOutputStore))]
    [InlineData(typeof(IDurableOutputDeliveryStore))]
    [InlineData(typeof(IDurableOutputDeadLetterStore))]
    [InlineData(typeof(IDurableOutputStatusStore))]
    [InlineData(typeof(IDurableOutputRetentionStore))]
    public void Missing_provider_alias_is_rejected_without_repair(Type missingContract)
    {
        var services = ValidServices();
        var descriptor = services.Single(item => item.ServiceType == missingContract);
        services.Remove(descriptor);
        var before = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowTSqlDurableOutput(builder =>
                builder.ConnectionString = TSqlDurableOutputTestData.UnreachableConnectionString));

        exception.Message.ShouldContain("service ownership");
        services.ShouldBe(before);
        services.ShouldNotContain(item => item.ServiceType == missingContract);
    }

    [Fact]
    public void Tampered_concrete_registration_is_rejected_without_repair()
    {
        var services = ValidServices();
        var concrete = services.Single(item =>
            item.ServiceType == typeof(TSqlDurableOutputStore));
        services.Remove(concrete);
        services.AddSingleton<TSqlDurableOutputStore>(_ =>
            new TSqlDurableOutputStore(new TSqlDurableOutputStoreOptions
            {
                ConnectionString = TSqlDurableOutputTestData.UnreachableConnectionString
            }));
        var before = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowTSqlDurableOutput(builder =>
                builder.ConnectionString = TSqlDurableOutputTestData.UnreachableConnectionString));

        exception.Message.ShouldContain("service ownership");
        services.ShouldBe(before);
    }

    [Fact]
    public async Task Registration_build_and_resolution_are_side_effect_free_and_alias_one_singleton()
    {
        var marker = new object();
        var services = new ServiceCollection();
        services.AddSingleton(marker);
        services.AddFluxFlowTSqlDurableOutput(builder =>
            builder.ConnectionString = TSqlDurableOutputTestData.UnreachableConnectionString);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var concrete = provider.GetRequiredService<TSqlDurableOutputStore>();
        var capture = provider.GetRequiredService<IDurableOutputStore>();
        var delivery = provider.GetRequiredService<IDurableOutputDeliveryStore>();
        var deadLetters = provider.GetRequiredService<IDurableOutputDeadLetterStore>();
        var status = provider.GetRequiredService<IDurableOutputStatusStore>();
        var retention = provider.GetRequiredService<IDurableOutputRetentionStore>();

        capture.ShouldBeSameAs(concrete);
        delivery.ShouldBeSameAs(concrete);
        deadLetters.ShouldBeSameAs(concrete);
        status.ShouldBeSameAs(concrete);
        retention.ShouldBeSameAs(concrete);
        provider.GetRequiredService<TSqlDurableOutputStore>().ShouldBeSameAs(concrete);
        provider.GetRequiredService<object>().ShouldBeSameAs(marker);
        AssertExactProviderShape(services, additionalDescriptors: 1);
    }

    private static ServiceCollection ValidServices()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowTSqlDurableOutput(builder =>
            builder.ConnectionString = TSqlDurableOutputTestData.UnreachableConnectionString);
        return services;
    }

    private static void AssertExactProviderShape(
        IServiceCollection services,
        int additionalDescriptors = 0)
    {
        services.Count.ShouldBe(7 + additionalDescriptors);
        services.Count(item => item.ServiceType == typeof(TSqlDurableOutputStoreOptions)).ShouldBe(1);
        services.Count(item => item.ServiceType == typeof(TSqlDurableOutputStore)).ShouldBe(1);
        services.Count(item => item.ServiceType == typeof(IDurableOutputStore)).ShouldBe(1);
        services.Count(item => item.ServiceType == typeof(IDurableOutputDeliveryStore)).ShouldBe(1);
        services.Count(item => item.ServiceType == typeof(IDurableOutputDeadLetterStore)).ShouldBe(1);
        services.Count(item => item.ServiceType == typeof(IDurableOutputStatusStore)).ShouldBe(1);
        services.Count(item => item.ServiceType == typeof(IDurableOutputRetentionStore)).ShouldBe(1);
        services.Where(item =>
                item.ServiceType == typeof(TSqlDurableOutputStoreOptions) ||
                item.ServiceType == typeof(TSqlDurableOutputStore) ||
                item.ServiceType == typeof(IDurableOutputStore) ||
                item.ServiceType == typeof(IDurableOutputDeliveryStore) ||
                item.ServiceType == typeof(IDurableOutputDeadLetterStore) ||
                item.ServiceType == typeof(IDurableOutputStatusStore) ||
                item.ServiceType == typeof(IDurableOutputRetentionStore))
            .ShouldAllBe(item => item.Lifetime == ServiceLifetime.Singleton);
    }

    private sealed class IntentionalConfigurationException(string message) :
        Exception(message);
}
