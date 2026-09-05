using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.TSql.Tests;

public sealed class TSqlDurableInputRegistrationTests
{
    [Fact]
    public void Registration_returns_original_collection_invokes_callback_once_and_snapshots_options()
    {
        var services = new ServiceCollection();
        var calls = 0;
        TSqlDurableInputStoreOptionsBuilder? captured = null;

        var returned = services.AddFluxFlowTSqlDurableInput(builder =>
        {
            calls++;
            captured = builder;
            builder.ConnectionString =
                " Server=database.example.test;Database=FluxFlow;Encrypt=False ";
            builder.CommandTimeout = TimeSpan.FromSeconds(8);
            builder.SchemaLockTimeout = TimeSpan.FromMilliseconds(1234);
            builder.ConnectRetryCount = 3;
            builder.ConnectRetryInterval = TimeSpan.FromSeconds(9);
            builder.SchemaManagement = TSqlDurableInputSchemaManagement.ValidateOnly;
        });

        captured.ShouldNotBeNull();
        captured.ConnectionString = TSqlDurableInputTestData.UnreachableConnectionString;
        captured.CommandTimeout = TimeSpan.FromSeconds(1);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<TSqlDurableInputStoreOptions>();

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
        options.SchemaManagement.ShouldBe(TSqlDurableInputSchemaManagement.ValidateOnly);
    }

    [Fact]
    public void Registration_rejects_null_inputs_before_mutation()
    {
        var services = new ServiceCollection();

        var nullServices = Should.Throw<ArgumentNullException>(() =>
            TSqlDurableInputServiceCollectionExtensions.AddFluxFlowTSqlDurableInput(null!, _ => { }));
        var nullConfigure = Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowTSqlDurableInput(null!));

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
            services.AddFluxFlowTSqlDurableInput(_ =>
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
            services.AddFluxFlowTSqlDurableInput(builder =>
            {
                builder.ConnectionString = TSqlDurableInputTestData.UnreachableConnectionString;
                builder.CommandTimeout = TimeSpan.Zero;
            }));

        exception.ParamName.ShouldBe(nameof(TSqlDurableInputStoreOptions.CommandTimeout));
        services.ShouldBe(before);
    }

    [Fact]
    public void Semantically_equivalent_normalized_registration_is_idempotent()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowTSqlDurableInput(builder =>
        {
            builder.ConnectionString =
                " Server=database.example.test;Database=FluxFlow;Encrypt=False;Application Name=Host ";
            builder.ConnectRetryCount = 2;
            builder.ConnectRetryInterval = TimeSpan.FromSeconds(7);
        });
        var before = services.ToArray();

        var returned = services.AddFluxFlowTSqlDurableInput(builder =>
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
            services.AddFluxFlowTSqlDurableInput(builder =>
            {
                builder.ConnectionString = TSqlDurableInputTestData.UnreachableConnectionString;
                builder.CommandTimeout = TimeSpan.FromSeconds(31);
            }));

        exception.Message.ShouldContain("different options");
        services.ShouldBe(before);
    }

    [Theory]
    [InlineData(typeof(IDurableInputStore))]
    [InlineData(typeof(IDurableInputDeadLetterStore))]
    [InlineData(typeof(IDurableInputLeaseRenewalStore))]
    [InlineData(typeof(IDurableInputStatusStore))]
    [InlineData(typeof(IDurableInputRetentionStore))]
    public void Preexisting_interface_registration_conflicts_atomically(Type contract)
    {
        var marker = new object();
        var services = new ServiceCollection();
        ((ICollection<ServiceDescriptor>)services).Add(ServiceDescriptor.Singleton(contract, _ => marker));
        var before = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowTSqlDurableInput(builder =>
                builder.ConnectionString = TSqlDurableInputTestData.UnreachableConnectionString));

        exception.Message.ShouldContain(contract.Name);
        services.ShouldBe(before);
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(TSqlDurableInputStore));
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(TSqlDurableInputStoreOptions));
    }

    [Theory]
    [InlineData(typeof(IDurableInputStore))]
    [InlineData(typeof(IDurableInputDeadLetterStore))]
    [InlineData(typeof(IDurableInputLeaseRenewalStore))]
    [InlineData(typeof(IDurableInputStatusStore))]
    [InlineData(typeof(IDurableInputRetentionStore))]
    public void Missing_provider_alias_is_rejected_without_repair(Type missingContract)
    {
        var services = ValidServices();
        services.Remove(services.Single(item => item.ServiceType == missingContract));
        var before = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowTSqlDurableInput(builder =>
                builder.ConnectionString = TSqlDurableInputTestData.UnreachableConnectionString));

        exception.Message.ShouldContain("service ownership");
        services.ShouldBe(before);
        services.ShouldNotContain(item => item.ServiceType == missingContract);
    }

    [Fact]
    public void Tampered_or_lifetime_incompatible_provider_registration_is_rejected_without_repair()
    {
        var services = ValidServices();
        services.Remove(services.Single(item => item.ServiceType == typeof(TSqlDurableInputStore)));
        services.AddTransient(_ => new TSqlDurableInputStore(new TSqlDurableInputStoreOptions
        {
            ConnectionString = TSqlDurableInputTestData.UnreachableConnectionString
        }));
        var before = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowTSqlDurableInput(builder =>
                builder.ConnectionString = TSqlDurableInputTestData.UnreachableConnectionString));

        exception.Message.ShouldContain("service ownership");
        services.ShouldBe(before);
    }

    [Fact]
    public async Task Registration_build_and_resolution_are_side_effect_free_and_alias_one_singleton()
    {
        var services = ValidServices();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var concrete = provider.GetRequiredService<TSqlDurableInputStore>();

        provider.GetRequiredService<IDurableInputStore>().ShouldBeSameAs(concrete);
        provider.GetRequiredService<IDurableInputDeadLetterStore>().ShouldBeSameAs(concrete);
        provider.GetRequiredService<IDurableInputLeaseRenewalStore>().ShouldBeSameAs(concrete);
        provider.GetRequiredService<IDurableInputStatusStore>().ShouldBeSameAs(concrete);
        provider.GetRequiredService<IDurableInputRetentionStore>().ShouldBeSameAs(concrete);
        provider.GetRequiredService<TSqlDurableInputStore>().ShouldBeSameAs(concrete);
        AssertExactProviderShape(services);
    }

    private static ServiceCollection ValidServices()
    {
        var services = new ServiceCollection();
        services.AddFluxFlowTSqlDurableInput(builder =>
            builder.ConnectionString = TSqlDurableInputTestData.UnreachableConnectionString);
        return services;
    }

    private static void AssertExactProviderShape(IServiceCollection services)
    {
        services.Count.ShouldBe(7);
        services.Count(item => item.ServiceType == typeof(TSqlDurableInputStoreOptions)).ShouldBe(1);
        services.Count(item => item.ServiceType == typeof(TSqlDurableInputStore)).ShouldBe(1);
        services.Count(item => item.ServiceType == typeof(IDurableInputStore)).ShouldBe(1);
        services.Count(item => item.ServiceType == typeof(IDurableInputDeadLetterStore)).ShouldBe(1);
        services.Count(item => item.ServiceType == typeof(IDurableInputLeaseRenewalStore)).ShouldBe(1);
        services.Count(item => item.ServiceType == typeof(IDurableInputStatusStore)).ShouldBe(1);
        services.Count(item => item.ServiceType == typeof(IDurableInputRetentionStore)).ShouldBe(1);
        services.ShouldAllBe(item => item.Lifetime == ServiceLifetime.Singleton);
    }

    private sealed class IntentionalConfigurationException(string message) : Exception(message);
}
