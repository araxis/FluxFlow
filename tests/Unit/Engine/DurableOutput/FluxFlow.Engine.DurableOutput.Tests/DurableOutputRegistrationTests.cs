using System.Text.Json;
using System.Reflection;
using FluxFlow.Data;
using FluxFlow.Nodes;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Definition;
using FluxFlow.Engine.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableOutput.Tests;

public sealed class DurableOutputRegistrationTests
{
    [Fact]
    public void Durable_output_handle_surface_accepts_only_typed_outputs()
    {
        var handleParameters = typeof(DurableOutputRegistrationBuilder)
            .GetMethods()
            .Where(static method => method.Name == nameof(DurableOutputRegistrationBuilder.Capture))
            .Select(static method => method.GetParameters()[0].ParameterType)
            .Where(static type => typeof(PortHandle).IsAssignableFrom(type) || type.IsGenericType)
            .ToArray();

        handleParameters.ShouldHaveSingleItem()
            .GetGenericTypeDefinition().ShouldBe(typeof(OutputPortHandle<>));
        handleParameters.ShouldNotContain(typeof(SignalInputPortHandle));
    }

    [Fact]
    public void Typed_output_handle_capture_uses_exact_address_and_atomic_conflict_rules()
    {
        var output = CreateOutputHandle();
        var builder = new DurableOutputRegistrationBuilder();

        var first = builder.Capture(output, "text-v1");
        var duplicate = builder.Capture(output, "text-v1");
        var beforeConflict = builder.Build().Captures.ShouldHaveSingleItem().Value;
        var conflict = Should.Throw<InvalidOperationException>(() =>
            builder.Capture(output, "other-v1"));
        var afterConflict = builder.Build().Captures.ShouldHaveSingleItem().Value;

        first.ShouldBeSameAs(builder);
        duplicate.ShouldBeSameAs(builder);
        beforeConflict.Address.ShouldBe(output.Address);
        beforeConflict.ContractName.ShouldBe("text-v1");
        conflict.Message.ShouldContain(output.Address.Value);
        afterConflict.ShouldBeSameAs(beforeConflict);
        (Should.Throw<ArgumentNullException>(() =>
            builder.Capture((OutputPortHandle<string>)null!, "text-v1")))
            .ParamName.ShouldBe("output");
    }

    [Fact]
    public void Builder_captures_exact_flat_declarations_and_equivalent_duplicate_is_idempotent()
    {
        var builder = new DurableOutputRegistrationBuilder();

        var first = builder.Capture(
            DurableOutputTestData.Output,
            "text-v1");
        var duplicate = builder.Capture(
            DurableOutputTestData.Output.Value,
            "text-v1");
        var configuration = builder.Build();

        first.ShouldBeSameAs(builder);
        duplicate.ShouldBeSameAs(builder);
        var definition = configuration.Captures.ShouldHaveSingleItem().Value;
        definition.Address.ShouldBe(DurableOutputTestData.Output);
        definition.ContractName.ShouldBe("text-v1");
    }

    [Fact]
    public void Builder_rejects_empty_configuration_and_invalid_declarations()
    {
        Should.Throw<InvalidOperationException>(() =>
            new DurableOutputRegistrationBuilder().Build());

        Should.Throw<ArgumentNullException>(() =>
            new DurableOutputRegistrationBuilder().Capture(
                (ApplicationAddress)null!,
                "text-v1"));
        Should.Throw<ArgumentException>(() =>
            new DurableOutputRegistrationBuilder().Capture(
                ApplicationAddress.Resource("store"),
                "text-v1")).ParamName.ShouldBe("output");
        Should.Throw<ArgumentNullException>(() =>
            new DurableOutputRegistrationBuilder().Capture(
                DurableOutputTestData.Output,
                null!)).ParamName.ShouldBe("contractName");
        Should.Throw<ArgumentException>(() =>
            new DurableOutputRegistrationBuilder().Capture(
                DurableOutputTestData.Output,
                " text-v1")).ParamName.ShouldBe("contractName");
    }

    [Fact]
    public void Builder_rejects_address_and_contract_conflicts_without_losing_prior_declaration()
    {
        var builder = new DurableOutputRegistrationBuilder()
            .Capture(DurableOutputTestData.Output, "text-v1");

        Should.Throw<InvalidOperationException>(() => builder.Capture(
            DurableOutputTestData.Output,
            "other-v1"));

        var definition = builder.Build().Captures.ShouldHaveSingleItem().Value;
        definition.Address.ShouldBe(DurableOutputTestData.Output);
        definition.ContractName.ShouldBe("text-v1");
    }

    [Fact]
    public void Same_contract_can_capture_multiple_explicit_outputs()
    {
        var configuration = new DurableOutputRegistrationBuilder()
            .Capture(DurableOutputTestData.Output, "text-v1")
            .Capture(DurableOutputTestData.SecondOutput, "text-v1")
            .Build();

        configuration.Captures.Keys.ShouldBe(
            [DurableOutputTestData.Output, DurableOutputTestData.SecondOutput],
            ignoreOrder: true);
        configuration.Captures.Values.Select(static capture => capture.ContractName)
            .ShouldAllBe(static contractName => contractName == "text-v1");
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
        var services = new ServiceCollection();
        services.AddSingleton<IDurableOutputStore>(store);
        services.AddSingleton<TimeProvider>(clock);

        var first = services.AddFluxFlowDurableOutput(builder => builder.Capture(
            DurableOutputTestData.Output,
            "text-v1"));
        var countAfterFirst = services.Count;
        var second = services.AddFluxFlowDurableOutput(builder => builder.Capture(
            DurableOutputTestData.Output,
            "text-v1"));

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
        var services = new ServiceCollection();
        services.AddSingleton<IDurableOutputStore>(new RecordingDurableOutputStore());
        services.AddFluxFlowDurableOutput(builder => builder.Capture(
            DurableOutputTestData.Output,
            "text-v1"));
        var before = services.ToArray();

        var exception = Should.Throw<InvalidOperationException>(() =>
            services.AddFluxFlowDurableOutput(builder => builder.Capture(
                DurableOutputTestData.SecondOutput,
                "text-v1")));

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
                "text-v1")));

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
            "text-v1"));
        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<InvalidOperationException>(() =>
            provider.GetRequiredService<IApplicationOutputCaptureResolver>());

        exception.Message.ShouldContain(expectedMessage);
    }

    [Fact]
    public void Resolver_returns_only_exact_configured_address()
    {
        var configuration = new DurableOutputRegistrationBuilder()
            .Capture(
                DurableOutputTestData.Output,
                "text-v1")
            .Build();
        var resolver = new DurableOutputCaptureResolver(
            configuration,
            new RecordingDurableOutputStore(),
            new FakeTimeProvider(DurableOutputTestData.CapturedAt));

        resolver.Resolve(DurableOutputTestData.Output)
            .ShouldBeOfType<DurableOutputCapture>();
        resolver.Resolve(DurableOutputTestData.SecondOutput).ShouldBeNull();
        Should.Throw<ArgumentNullException>(() => resolver.Resolve(null!))
            .ParamName.ShouldBe("address");
    }

    [Fact]
    public void Runtime_capture_types_hold_explicit_dependencies_without_service_locator_or_provider_reference()
    {
        var runtimeTypes = new[]
        {
            typeof(DurableOutputCaptureResolver),
            typeof(DurableOutputCapture)
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

    [Fact]
    public async Task Resolved_capture_accepts_canonical_shapes_without_clr_type_registration()
    {
        var store = new RecordingDurableOutputStore();
        var services = new ServiceCollection();
        services.AddSingleton<IDurableOutputStore>(store);
        services.AddFluxFlowDurableOutput(outputs =>
            outputs.Capture(DurableOutputTestData.Output, "dynamic-v1"));
        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IApplicationOutputCaptureResolver>();
        var capture = resolver.Resolve(DurableOutputTestData.Output).ShouldNotBeNull();
        var values = new[] { ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("text", new JsonSerializerOptions(JsonSerializerDefaults.Web)))), ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new { answer = 42 }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), JsonFlowValue.Null };
        var messages = values.Select(value => FlowMessage.Create(value)).ToArray();

        foreach (var message in messages)
            await capture.CaptureAsync(message);

        resolver.Resolve(DurableOutputTestData.SecondOutput).ShouldBeNull();
        store.Envelopes.Count.ShouldBe(messages.Length);
        store.Envelopes.Select(envelope => envelope.MessageId)
            .ShouldBe(messages.Select(message => message.MessageId));
        store.Envelopes.Select(envelope => envelope.Payload.GetRawText())
            .ShouldBe(["\"text\"", "{\"answer\":42}", "null"]);
        store.Envelopes.ShouldAllBe(envelope =>
            envelope.Address == DurableOutputTestData.Output && envelope.ContractName == "dynamic-v1");
    }

    private sealed class NullResolver : IApplicationOutputCaptureResolver
    {
        public IApplicationOutputCapture? Resolve(ApplicationAddress address) => null;
    }

    private static OutputPortHandle<string> CreateOutputHandle()
    {
        var application = new ApplicationDefinitionBuilder();
        return application
            .AddWorkflow("Orders")
            .AddComponent("Publisher", "test.publisher")
            .Output<string>("Output");
    }
}
