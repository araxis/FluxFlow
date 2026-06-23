using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Payloads;
using FluxFlow.Components.Payloads.Composition;
using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Diagnostics;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Payloads.Composition.Tests;

public sealed class PayloadsCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void RegisterPayloadInspect_registers_request_result_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterPayloadInspect();

        var registration = registry.Registrations[PayloadsCompositionNodeTypes.Inspect];
        registration.Inputs[PayloadsCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(PayloadInspectionRequest));
        registration.Outputs[PayloadsCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(PayloadInspectionResult));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_payload_metadata()
    {
        var metadata = PayloadDesignMetadata();

        metadata.Type.Value.ShouldBe(PayloadsCompositionNodeTypes.Inspect);
        metadata.DisplayName.ShouldBe("Payload Inspect");
        metadata.Category.ShouldBe("Payloads");
        metadata.SuggestedEditorWidth.ShouldBe(420);
        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Options.ShouldNotContain(option =>
            option.Name == PayloadsCompositionResourceNames.Clock);
    }

    [Fact]
    public void Design_metadata_provider_describes_payload_ports()
    {
        var metadata = PayloadDesignMetadata();

        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(PayloadsCompositionPortNames.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType.ShouldBe(nameof(PayloadInspectionRequest));
        input.IsPrimary.ShouldBeTrue();

        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(PayloadsCompositionPortNames.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(1);
        output.ValueType.ShouldBe(nameof(PayloadInspectionResult));
        output.IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public void Design_metadata_provider_describes_payload_options()
    {
        var metadata = PayloadDesignMetadata();
        var defaults = PayloadInspectOptions.Default;

        metadata.Options.Select(option => option.Name).ShouldBe([
            "maxInputBytes",
            "maxPreviewBytes",
            "maxFormattedChars",
            "detectBase64",
            "formatJson",
            "formatXml",
            "boundedCapacity"
        ], ignoreOrder: false);

        AssertOption(
            metadata,
            "maxInputBytes",
            OptionValueKind.Number,
            defaults.MaxInputBytes,
            min: 1);
        AssertOption(
            metadata,
            "maxPreviewBytes",
            OptionValueKind.Number,
            defaults.MaxPreviewBytes,
            min: 1);
        AssertOption(
            metadata,
            "maxFormattedChars",
            OptionValueKind.Number,
            defaults.MaxFormattedChars,
            min: 1);
        AssertOption(
            metadata,
            "detectBase64",
            OptionValueKind.Boolean,
            defaults.DetectBase64);
        AssertOption(
            metadata,
            "formatJson",
            OptionValueKind.Boolean,
            defaults.FormatJson);
        AssertOption(
            metadata,
            "formatXml",
            OptionValueKind.Boolean,
            defaults.FormatXml);
        AssertOption(
            metadata,
            "boundedCapacity",
            OptionValueKind.Number,
            defaults.BoundedCapacity,
            min: 1);
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var provider = new PayloadsComponentDesignMetadataProvider();
        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.All.ShouldHaveSingleItem();
        catalog.TryGet(
            new ComponentType(PayloadsCompositionNodeTypes.Inspect),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull()
            .DisplayName.ShouldBe("Payload Inspect");
    }

    [Fact]
    public async Task Hosted_payload_inspect_classifies_json_and_preserves_correlation_id()
    {
        var result = await RunNodeAsync(
            new PayloadInspectionRequest
            {
                Text = """{"name":"flux","count":2}""",
                ContentType = "application/json"
            },
            node => node.Configure("maxPreviewBytes", 128));

        result.CorrelationId.ShouldBe(new CorrelationId("payload.inspect"));
        result.Payload.Kind.ShouldBe(PayloadKind.JsonObject);
        result.Payload.ContentType.ShouldBe("application/json");
        result.Payload.TextPreview.ShouldNotBeNull().ShouldContain("\"name\"");
        result.Payload.FormattedPreview.ShouldNotBeNull().ShouldContain("\n");
    }

    [Fact]
    public async Task Hosted_payload_inspect_binds_options_from_configuration()
    {
        var result = await RunNodeAsync(
            new PayloadInspectionRequest
            {
                Text = """{"message":"abcdef"}""",
                ContentType = "application/json"
            },
            node => node
                .Configure("maxPreviewBytes", 3)
                .Configure("maxFormattedChars", 10));

        result.Payload.TextPreview.ShouldBe("""{"m""");
        result.Payload.TextPreviewTruncated.ShouldBeTrue();
        result.Payload.FormattedPreview.ShouldNotBeNull();
        result.Payload.FormattedPreview!.Length.ShouldBe(10);
        result.Payload.FormattedPreviewTruncated.ShouldBeTrue();
    }

    [Fact]
    public async Task Hosted_payload_inspect_uses_optional_keyed_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);

        var result = await RunNodeAsync(
            new PayloadInspectionRequest { Text = "hello" },
            node => node.Resource(PayloadsCompositionResourceNames.Clock, "fixed"),
            services => services.AddKeyedSingleton<TimeProvider>("fixed", clock));

        result.Payload.Timestamp.ShouldBe(timestamp);
    }

    [Fact]
    public async Task Hosted_payload_inspect_emits_errors_and_continues()
    {
        await WithNodeAsync(async (input, output, descriptor) =>
        {
            var results = Link(output.Source);
            var errors = Link(descriptor.Errors.ShouldNotBeNull());
            var bad = FlowMessage.Create(
                new PayloadInspectionRequest
                {
                    Text = "hello",
                    EncodingHint = "missing-encoding"
                },
                new CorrelationId("bad"));
            var good = FlowMessage.Create(
                new PayloadInspectionRequest { Text = "hello" },
                new CorrelationId("good"));

            (await input.Target.SendAsync(bad).WaitAsync(Timeout)).ShouldBeTrue();
            (await input.Target.SendAsync(good).WaitAsync(Timeout)).ShouldBeTrue();

            var error = await errors.ReceiveAsync().WaitAsync(Timeout);
            var result = await results.ReceiveAsync().WaitAsync(Timeout);

            error.Code.ShouldBe(PayloadErrorCodes.UnsupportedEncoding);
            error.CorrelationId.ShouldBe(bad.CorrelationId);
            result.CorrelationId.ShouldBe(good.CorrelationId);
            result.Payload.Kind.ShouldBe(PayloadKind.Text);
            result.Payload.TextPreview.ShouldBe("hello");
        });
    }

    [Fact]
    public async Task Hosted_payload_inspect_exposes_events()
    {
        await WithNodeAsync(async (input, output, descriptor) =>
        {
            output.Source.LinkTo(DataflowBlock.NullTarget<FlowMessage<PayloadInspectionResult>>());
            var events = Link(descriptor.Events.ShouldNotBeNull());
            var message = FlowMessage.Create(new PayloadInspectionRequest { Text = "hello" });

            (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();

            var @event = await events.ReceiveAsync().WaitAsync(Timeout);
            @event.Name.ShouldBe(PayloadDiagnosticNames.Inspected);
            @event.CorrelationId.ShouldBe(message.CorrelationId);
        });
    }

    [Fact]
    public async Task Invalid_configuration_surfaces_factory_diagnostic()
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "inspect",
                    PayloadsCompositionNodeTypes.Inspect,
                    node => node.Configure("boundedCapacity", 0)))
                .Build())
            .RegisterNodes(registry => registry.RegisterPayloadInspect())
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(
                "boundedCapacity",
                StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<FlowMessage<PayloadInspectionResult>> RunNodeAsync(
        PayloadInspectionRequest request,
        Action<NodeDefinitionBuilder>? configureNode = null,
        Action<IServiceCollection>? configureServices = null)
    {
        FlowMessage<PayloadInspectionResult>? result = null;
        await WithNodeAsync(
            async (input, output, _) =>
            {
                var results = Link(output.Source);
                var message = FlowMessage.Create(
                    request,
                    new CorrelationId(PayloadsCompositionNodeTypes.Inspect));

                (await input.Target.SendAsync(message).WaitAsync(Timeout))
                    .ShouldBeTrue();

                result = await results.ReceiveAsync().WaitAsync(Timeout);
            },
            configureNode,
            configureServices);

        return result.ShouldNotBeNull();
    }

    private static ComponentDesignMetadata PayloadDesignMetadata()
        => new PayloadsComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

    private static void AssertOption(
        ComponentDesignMetadata metadata,
        string name,
        OptionValueKind kind,
        object? defaultValue,
        double? min = null)
    {
        var option = metadata.Options.Single(option => option.Name == name);
        option.Kind.ShouldBe(kind);
        option.DefaultValue.ShouldBe(defaultValue);
        option.Min.ShouldBe(min);
    }

    private static async Task WithNodeAsync(
        Func<
            CompositionInputPort<PayloadInspectionRequest>,
            CompositionOutputPort<PayloadInspectionResult>,
            ComposedNode,
            Task> run,
        Action<NodeDefinitionBuilder>? configureNode = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "node",
                    PayloadsCompositionNodeTypes.Inspect,
                    configureNode))
                .Build())
            .RegisterNodes(registry => registry.RegisterPayloadInspect())
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var descriptor = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem()
            .Descriptor;
        var input = descriptor.Inputs[PayloadsCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<PayloadInspectionRequest>>();
        var output = descriptor.Outputs[PayloadsCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<PayloadInspectionResult>>();

        await run(input, output, descriptor);
    }

    private static async Task BuildCompositionAsync(IServiceProvider provider)
    {
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();
        await hostedService.StartAsync(CancellationToken.None);
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }
}
