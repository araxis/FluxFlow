using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Model;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Composition.Tests;

public sealed class TypedCodeFirstResourceAuthoringTests
{
    [Fact]
    public void Executable_resource_contracts_capture_definition_and_factory_without_activation()
    {
        var registrar = new RecordingRegistrar();
        var optionCreations = 0;
        var optionApplications = 0;
        var handleCreations = 0;
        var configured = ApplicationResourceContract.Create<ResourceOptions, TestResourceHandle>(
            "test.configured",
            registrar,
            () =>
            {
                optionCreations++;
                return new ResourceOptions();
            },
            (options, definition) =>
            {
                optionApplications++;
                definition.Set("Endpoint", options.Endpoint);
                definition.Set("Enabled", options.Enabled);
            },
            definition =>
            {
                handleCreations++;
                return new TestResourceHandle(definition);
            });
        var simple = ApplicationResourceContract.Create(
            "test.simple",
            registrar,
            static definition => new TestResourceHandle(definition));
        var application = new ApplicationDefinitionBuilder();

        var returned = application.AddResource(
            "configured",
            configured,
            options =>
            {
                options.Endpoint = "memory://configured";
                options.Enabled = true;
            },
            out var configuredHandle);
        var simpleHandle = application.AddResource("simple", simple);
        var definition = application.Build();

        returned.ShouldBeSameAs(application);
        configured.Type.ShouldBe("test.configured");
        configuredHandle.Address.Value.ShouldBe("Resources.configured");
        configuredHandle.Name.ShouldBe("configured");
        configuredHandle.Type.ShouldBe(configured.Type);
        configuredHandle.Definition.Type.ShouldBe(configured.Type);
        simpleHandle.Address.Value.ShouldBe("Resources.simple");
        simpleHandle.Type.ShouldBe(simple.Type);
        optionCreations.ShouldBe(1);
        optionApplications.ShouldBe(1);
        handleCreations.ShouldBe(1);
        registrar.RegistrationCount.ShouldBe(0);

        var configuredDefinition = definition.Resources["configured"]
            .ShouldBeOfType<ResourceInstanceDefinition>();
        configuredDefinition.Type.ShouldBe(configured.Type);
        configuredDefinition.Properties["Endpoint"].GetString()
            .ShouldBe("memory://configured");
        configuredDefinition.Properties["Enabled"].GetBoolean().ShouldBeTrue();
        definition.Resources["simple"].ShouldBeOfType<ResourceInstanceDefinition>()
            .Type.ShouldBe(simple.Type);
        definition.ApplicationResourceContracts.ShouldBe([configured, simple]);
        registrar.RegistrationCount.ShouldBe(0);
    }

    [Fact]
    public void Resource_contract_configuration_failures_are_atomic_and_allow_same_name_retry()
    {
        var registrar = new RecordingRegistrar();
        var valid = Contract("test.valid", registrar);
        var nullOptions = ApplicationResourceContract.Create<ResourceOptions, TestResourceHandle>(
            "test.null-options",
            registrar,
            static () => null!,
            static (_, _) => { },
            static definition => new TestResourceHandle(definition));
        var nullHandle = ApplicationResourceContract.Create<TestResourceHandle>(
            "test.null-handle",
            registrar,
            static _ => null!);
        var application = new ApplicationDefinitionBuilder();

        var configureFailure = Should.Throw<InvalidOperationException>(() =>
            application.AddResource(
                "resource",
                valid,
                _ => throw new InvalidOperationException("configure failed")));
        configureFailure.Message.ShouldBe("configure failed");
        application.AddResource("resource", valid, static options =>
            options.Endpoint = "memory://recovered");

        Should.Throw<InvalidOperationException>(() =>
            application.AddResource("null-options", nullOptions, static _ => { }))
            .Message.ShouldContain("returned no options builder");
        application.AddResource("null-options", valid, static options =>
            options.Endpoint = "memory://options-retry");

        Should.Throw<InvalidOperationException>(() =>
            application.AddResource("null-handle", nullHandle))
            .Message.ShouldContain("returned no handle");
        application.AddResource("null-handle", valid, static options =>
            options.Endpoint = "memory://handle-retry");

        var definition = application.Build();

        definition.Resources.Keys.ShouldBe(
            ["resource", "null-options", "null-handle"],
            ignoreOrder: true);
        definition.ApplicationResourceContracts.ShouldHaveSingleItem().ShouldBeSameAs(valid);
        definition.Resources.Values
            .Cast<ResourceInstanceDefinition>()
            .Select(static resource => resource.Properties["Endpoint"].GetString())
            .ShouldBe([
                "memory://recovered",
                "memory://options-retry",
                "memory://handle-retry"
            ], ignoreOrder: true);
        registrar.RegistrationCount.ShouldBe(0);
    }

    [Fact]
    public void Exact_resource_contract_reuse_deduplicates_and_distinct_same_identity_contracts_conflict_atomically()
    {
        var registrar = new RecordingRegistrar();
        var retained = Contract("test.shared", registrar);
        var conflicting = Contract("test.shared", registrar);
        var application = new ApplicationDefinitionBuilder();
        application.AddResource("first", retained, static options =>
            options.Endpoint = "memory://first");
        var group = application.AddResourceGroup("nested");
        group.AddResource("second", retained, static options =>
            options.Endpoint = "memory://second");

        var conflict = Should.Throw<InvalidOperationException>(() =>
            application.AddResource("conflict", conflicting, static options =>
                options.Endpoint = "memory://conflict"));

        conflict.Message.ShouldContain(retained.Type);
        conflict.Message.ShouldContain("conflicting contracts");
        var definition = application.Build();
        definition.ApplicationResourceContracts.ShouldHaveSingleItem()
            .ShouldBeSameAs(retained);
        definition.Resources.Keys.ShouldBe(["first", "nested"], ignoreOrder: true);
        definition.Resources.ContainsKey("conflict").ShouldBeFalse();
        definition.Resources["nested"].ShouldBeOfType<ResourceGroupDefinition>()
            .Resources.Keys.ShouldBe(["second"]);
        registrar.RegistrationCount.ShouldBe(0);
    }

    [Fact]
    public void Built_resource_contracts_are_ordinal_read_only_and_owned_by_the_definition()
    {
        var registrar = new RecordingRegistrar();
        var zulu = Contract("test.zulu", registrar);
        var alpha = Contract("test.alpha", registrar);
        var application = new ApplicationDefinitionBuilder();
        application.AddResource("zulu", zulu, static options => options.Endpoint = "z");
        application.AddResource("alpha", alpha, static options => options.Endpoint = "a");

        var definition = application.Build();

        definition.ApplicationResourceContracts.ShouldBe([alpha, zulu]);
        definition.ApplicationResourceContracts[0].ShouldBeSameAs(alpha);
        definition.ApplicationResourceContracts[1].ShouldBeSameAs(zulu);
        var collection = definition.ApplicationResourceContracts
            .ShouldBeAssignableTo<ICollection<ApplicationResourceContract>>()!;
        collection.IsReadOnly.ShouldBeTrue();
        Should.Throw<NotSupportedException>(() => collection.Add(alpha));
        Should.Throw<InvalidOperationException>(() =>
            application.AddResource("late", alpha, static options => options.Endpoint = "late"));
        registrar.RegistrationCount.ShouldBe(0);
    }

    [Fact]
    public void Resource_contract_factories_validate_all_arguments_and_preserve_registrar_identity()
    {
        var registrar = new RecordingRegistrar();
        var contract = Contract("test.identity", registrar);

        ((IApplicationResourceRegistrar)contract).RegistrationIdentity
            .ShouldBeSameAs(registrar);
        typeof(ApplicationResourceContract).IsAbstract.ShouldBeTrue();
        typeof(ApplicationResourceContract<>).IsClass.ShouldBeTrue();
        typeof(ApplicationResourceContract<,>).IsClass.ShouldBeTrue();

        Should.Throw<ArgumentException>(() => ApplicationResourceContract.Create(
            " ", registrar, static definition => new TestResourceHandle(definition)))
            .ParamName.ShouldBe("type");
        Should.Throw<ArgumentException>(() => ApplicationResourceContract.Create(
            " test ", registrar, static definition => new TestResourceHandle(definition)))
            .ParamName.ShouldBe("type");
        Should.Throw<ArgumentNullException>(() => ApplicationResourceContract.Create(
            "test", null!, static definition => new TestResourceHandle(definition)))
            .ParamName.ShouldBe("registrar");
        Should.Throw<ArgumentNullException>(() => ApplicationResourceContract.Create<TestResourceHandle>(
            "test", registrar, null!)).ParamName.ShouldBe("createHandle");
        Should.Throw<ArgumentNullException>(() => ApplicationResourceContract.Create<ResourceOptions, TestResourceHandle>(
            "test", registrar, null!, static (_, _) => { },
            static definition => new TestResourceHandle(definition)))
            .ParamName.ShouldBe("createOptions");
        Should.Throw<ArgumentNullException>(() => ApplicationResourceContract.Create<ResourceOptions, TestResourceHandle>(
            "test", registrar, static () => new ResourceOptions(), null!,
            static definition => new TestResourceHandle(definition)))
            .ParamName.ShouldBe("apply");
        Should.Throw<ArgumentNullException>(() => ApplicationResourceContract.Create<ResourceOptions, TestResourceHandle>(
            "test", registrar, static () => new ResourceOptions(), static (_, _) => { }, null!))
            .ParamName.ShouldBe("createHandle");
    }

    private static ApplicationResourceContract<ResourceOptions, TestResourceHandle> Contract(
        string type,
        IApplicationResourceRegistrar registrar)
        => ApplicationResourceContract.Create<ResourceOptions, TestResourceHandle>(
            type,
            registrar,
            static () => new ResourceOptions(),
            static (options, definition) => definition.Set("Endpoint", options.Endpoint),
            static definition => new TestResourceHandle(definition));

    private sealed class ResourceOptions
    {
        public string Endpoint { get; set; } = "memory://default";

        public bool Enabled { get; set; }
    }

    private sealed class TestResourceHandle(ResourceHandle definition)
        : AuthoredResourceHandle(definition);

    private sealed class RecordingRegistrar : IApplicationResourceRegistrar
    {
        public int RegistrationCount { get; private set; }

        public void Register(ApplicationResourceRegistrationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            RegistrationCount++;
            context.Services.AddKeyedSingleton(
                "resources.value",
                new RegisteredResource(context.RevisionId));
        }
    }

    private sealed record RegisteredResource(string RevisionId);
}
