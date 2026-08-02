using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FluxFlow.Composition.Addressing;
using FluxFlow.Engine.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

public sealed class DurableOutputRegistrationTests
{
    [Fact]
    public void Builder_captures_exact_flat_declarations_and_equivalent_duplicate_is_idempotent()
    {
        var typeInfo = DurableOutputTestData.TypeInfo<string>();
        var builder = new DurableOutputRegistrationBuilder();

        var first = builder.Capture(
            DurableOutputTestData.Output,
            "text-v1",
            typeInfo);
        var duplicate = builder.Capture(
            DurableOutputTestData.Output.Value,
            "text-v1",
            typeInfo);
        var configuration = builder.Build();

        first.ShouldBeSameAs(builder);
        duplicate.ShouldBeSameAs(builder);
        var definition = configuration.Captures.ShouldHaveSingleItem().Value;
        definition.Address.ShouldBe(DurableOutputTestData.Output);
        definition.ContractName.ShouldBe("text-v1");
        definition.PayloadType.ShouldBe(typeof(string));
        definition.JsonTypeInfo.ShouldBeSameAs(typeInfo);
    }

    [Fact]
    public void Builder_rejects_empty_configuration_and_invalid_declarations()
    {
        Should.Throw<InvalidOperationException>(() =>
            new DurableOutputRegistrationBuilder().Build());

        var typeInfo = DurableOutputTestData.TypeInfo<string>();
        Should.Throw<ArgumentNullException>(() =>
            new DurableOutputRegistrationBuilder().Capture<string>(
                (ApplicationAddress)null!,
                "text-v1",
                typeInfo));
        Should.Throw<ArgumentException>(() =>
            new DurableOutputRegistrationBuilder().Capture(
                ApplicationAddress.Resource("store"),
                "text-v1",
                typeInfo)).ParamName.ShouldBe("output");
        Should.Throw<ArgumentNullException>(() =>
            new DurableOutputRegistrationBuilder().Capture(
                DurableOutputTestData.Output,
                null!,
                typeInfo)).ParamName.ShouldBe("contractName");
        Should.Throw<ArgumentException>(() =>
            new DurableOutputRegistrationBuilder().Capture(
                DurableOutputTestData.Output,
                " text-v1",
                typeInfo)).ParamName.ShouldBe("contractName");
        Should.Throw<ArgumentNullException>(() =>
            new DurableOutputRegistrationBuilder().Capture<string>(
                DurableOutputTestData.Output,
                "text-v1",
                null!)).ParamName.ShouldBe("jsonTypeInfo");
    }

    [Fact]
    public void Builder_rejects_address_and_contract_conflicts_without_losing_prior_declaration()
    {
        var stringTypeInfo = DurableOutputTestData.TypeInfo<string>();
        var integerTypeInfo = DurableOutputTestData.TypeInfo<int>();
        var alternateStringTypeInfo = (JsonTypeInfo<string>)new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        }.GetTypeInfo(typeof(string));
        var builder = new DurableOutputRegistrationBuilder()
            .Capture(DurableOutputTestData.Output, "text-v1", stringTypeInfo);

        Should.Throw<InvalidOperationException>(() => builder.Capture(
            DurableOutputTestData.Output,
            "other-v1",
            stringTypeInfo));
        Should.Throw<InvalidOperationException>(() => builder.Capture(
            DurableOutputTestData.SecondOutput,
            "text-v1",
            integerTypeInfo));
        Should.Throw<InvalidOperationException>(() => builder.Capture(
            DurableOutputTestData.Output,
            "text-v1",
            alternateStringTypeInfo));

        var definition = builder.Build().Captures.ShouldHaveSingleItem().Value;
        definition.Address.ShouldBe(DurableOutputTestData.Output);
        definition.ContractName.ShouldBe("text-v1");
        definition.PayloadType.ShouldBe(typeof(string));
    }

    [Fact]
    public void Same_contract_and_payload_type_can_capture_multiple_explicit_outputs()
    {
        var typeInfo = DurableOutputTestData.TypeInfo<string>();
        var configuration = new DurableOutputRegistrationBuilder()
            .Capture(DurableOutputTestData.Output, "text-v1", typeInfo)
            .Capture(DurableOutputTestData.SecondOutput, "text-v1", typeInfo)
            .Build();

        configuration.Captures.Keys.ShouldBe(
            [DurableOutputTestData.Output, DurableOutputTestData.SecondOutput],
            ignoreOrder: true);
        configuration.Captures.Values.Select(static capture => capture.ContractName)
            .ShouldAllBe(static contractName => contractName == "text-v1");
        configuration.Captures.Values.Select(static capture => capture.PayloadType)
            .ShouldAllBe(static payloadType => payloadType == typeof(string));
    }

    [Fact]
    public void Service_registration_rejects_null_arguments_without_mutation()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentNullException>(() =>
            DurableOutputServiceCollectionExtensions.AddFluxFlowDurableOutput(
                null!,
                static _ => { })).ParamName.ShouldBe("services");
        Should.Throw<ArgumentNullException>(() =>
            services.AddFluxFlowDurableOutput(null!)).ParamName.ShouldBe("configure");
        services.ShouldBeEmpty();
    }

    [Fact]
    public void Equivalent_service_registration_is_idempotent_and_flat()
    {
        var store = new RecordingDurableOutputStore();
        var clock = new FakeTimeProvider(DurableOutputTestData.CapturedAt);
        var typeInfo = DurableOutputTestData.TypeInfo<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IDurableOutputStore>(store);
        services.AddSingleton<TimeProvider>(clock);

        var first = services.AddFluxFlowDurableOutput(builder => builder.Capture(
            DurableOutputTestData.Output,
            "text-v1",
            typeInfo));
        var countAfterFirst = services.Count;
        var second = services.AddFluxFlowDurableOutput(builder => builder.Capture(
            DurableOutputTestData.Output,
            "text-v1",
            typeInfo));

        first.ShouldBeSameAs(services);
        second.ShouldBeSameAs(services);
        services.Count.ShouldBe(countAfterFirst);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(DurableOutputConfiguration)).ShouldBe(1);
        services.Count(descriptor =>
            descriptor.ServiceType == typeof(IApplicationOutputCaptureResolver)).ShouldBe(1);
        services.Single(descriptor =>
            descriptor.ServiceType == typeof(IApplicationOutputCaptureResolver))
            .Lifetime.ShouldBe(ServiceLifetime.Singleton);
        services.Count(descriptor => descriptor.ServiceType == typeof(TimeProvider)).ShouldBe(1);
        services.Count(descriptor => descriptor.ServiceType == typeof(IDurableOutputStore)).ShouldBe(1);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<TimeProvider>().ShouldBeSameAs(clock);
        provider.GetRequiredService<IDurableOutputStore>().ShouldBeSameAs(store);
        provider.GetRequiredService<IApplicationOutputCaptureResolver>()
            .ShouldBeOfType<DurableOutputCaptureResolver>();
    }

    [Fact]
    public void Conflicting_repeat_registration_fails_without_partial_descriptors()
    {
        var typeInfo = DurableOutputTestData.TypeInfo<string>();
        var services = new ServiceCollection();
        services.AddSingleton<IDurableOutputStore>(new RecordingDurableOutputStore());
        services.AddFluxFlowDurableOutput(builder => builder.Capture(
            DurableOutputTestData.Output,
            "text-v1",
            typeInfo));
        var before = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowDurableOutput(builder => builder.Capture(
                DurableOutputTestData.SecondOutput,
                "text-v1",
                typeInfo)));

        exception.Message.ShouldContain("already registered with different declarations");
        services.ShouldBe(before);
    }

    [Fact]
    public void Existing_capture_resolver_conflicts_without_partial_descriptors()
    {
        var services = new ServiceCollection();
        var existing = new NullResolver();
        services.AddSingleton<IApplicationOutputCaptureResolver>(existing);
        var before = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowDurableOutput(builder => builder.Capture(
                DurableOutputTestData.Output,
                "text-v1",
                DurableOutputTestData.TypeInfo<string>())));

        exception.Message.ShouldContain("exclusive IApplicationOutputCaptureResolver ownership");
        services.ShouldBe(before);
    }

    [Theory]
    [InlineData(0, "requires one")]
    [InlineData(2, "exactly one")]
    public void Resolution_requires_exactly_one_store(int storeCount, string expectedMessage)
    {
        var services = new ServiceCollection();
        for (var index = 0; index < storeCount; index++)
            services.AddSingleton<IDurableOutputStore>(new RecordingDurableOutputStore());
        services.AddFluxFlowDurableOutput(builder => builder.Capture(
            DurableOutputTestData.Output,
            "text-v1",
            DurableOutputTestData.TypeInfo<string>()));
        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredService<IApplicationOutputCaptureResolver>());

        exception.Message.ShouldContain(expectedMessage);
    }

    [Fact]
    public void Resolver_returns_only_exact_configured_address_and_payload_type()
    {
        var configuration = new DurableOutputRegistrationBuilder()
            .Capture(
                DurableOutputTestData.Output,
                "text-v1",
                DurableOutputTestData.TypeInfo<string>())
            .Build();
        var resolver = new DurableOutputCaptureResolver(
            configuration,
            new RecordingDurableOutputStore(),
            new FakeTimeProvider(DurableOutputTestData.CapturedAt));

        resolver.Resolve<string>(DurableOutputTestData.Output)
            .ShouldBeOfType<DurableOutputCapture<string>>();
        resolver.Resolve<string>(DurableOutputTestData.SecondOutput).ShouldBeNull();
        Should.Throw<ArgumentNullException>(() => resolver.Resolve<string>(null!))
            .ParamName.ShouldBe("address");
        var mismatch = Should.Throw<InvalidOperationException>(() =>
            resolver.Resolve<int>(DurableOutputTestData.Output));
        mismatch.Message.ShouldContain(typeof(string).ToString());
        mismatch.Message.ShouldContain(typeof(int).ToString());
    }

    [Fact]
    public void Runtime_capture_types_hold_explicit_dependencies_without_service_locator_or_provider_reference()
    {
        var runtimeTypes = new[]
        {
            typeof(DurableOutputCaptureResolver),
            typeof(DurableOutputCapture<string>)
        };

        foreach (var type in runtimeTypes)
        {
            var fields = type.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            fields.Select(static field => field.FieldType)
                .ShouldNotContain(typeof(IServiceProvider), type.FullName);
            fields.Any(static field =>
                typeof(IServiceProvider).IsAssignableFrom(field.FieldType)).ShouldBeFalse(type.FullName);
        }

        var references = typeof(IDurableOutputStore).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .ToArray();
        references.ShouldNotContain("FluxFlow.Engine.DurableInput");
        references.ShouldNotContain("FluxFlow.Engine.DurableInput.SqlFile");
        references.ShouldNotContain("Microsoft.Data.Sqlite");
    }

    private sealed class NullResolver : IApplicationOutputCaptureResolver
    {
        public IApplicationOutputCapture<T>? Resolve<T>(ApplicationAddress address) => null;
    }
}
