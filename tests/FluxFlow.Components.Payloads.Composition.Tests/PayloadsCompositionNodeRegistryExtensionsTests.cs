using System.Collections.Immutable;
using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Payloads.Composition;
using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Diagnostics;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Data;
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
    public void RegisterPayloadInspect_registers_canonical_content_result_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterPayloadInspect();

        var registration = registry.Registrations[PayloadsCompositionNodeTypes.Inspect];
        registration.Inputs[PayloadsCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(FlowContent));
        registration.Outputs[PayloadsCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(FlowResult<PayloadInspectionResult>));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_payload_metadata()
    {
        var metadata = PayloadDesignMetadata();

        metadata.Type.Value.ShouldBe(PayloadsCompositionNodeTypes.Inspect);
        metadata.DisplayName?.Value.ShouldBe("Payload Inspect");
        metadata.Category.ShouldBe(new ComponentCategory("Payloads"));
        metadata.SuggestedEditorWidth.ShouldBe(420);
        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == PayloadsCompositionResourceNames.Codecs ||
            option.Name.Value == PayloadsCompositionResourceNames.Clock);
        AssertResources(metadata);
    }

    [Fact]
    public void Design_metadata_provider_describes_canonical_payload_ports()
    {
        var metadata = PayloadDesignMetadata();

        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(PayloadsCompositionPortNames.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType?.Value.ShouldBe(nameof(FlowContent));
        input.IsPrimary.ShouldBeTrue();

        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(PayloadsCompositionPortNames.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(1);
        output.ValueType?.Value.ShouldBe("FlowResult<PayloadInspectionResult>");
        output.IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public void Design_metadata_provider_describes_payload_options()
    {
        var metadata = PayloadDesignMetadata();
        var defaults = PayloadInspectOptions.Default;

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "maxInputBytes",
            "maxPreviewBytes",
            "maxFormattedChars",
            "detectBase64",
            "formatJson",
            "formatXml",
            "boundedCapacity"
        ], ignoreOrder: false);

        AssertOption(metadata, "maxInputBytes", OptionValueKind.Number, defaults.MaxInputBytes, 1);
        AssertOption(metadata, "maxPreviewBytes", OptionValueKind.Number, defaults.MaxPreviewBytes, 1);
        AssertOption(metadata, "maxFormattedChars", OptionValueKind.Number, defaults.MaxFormattedChars, 1);
        AssertOption(metadata, "detectBase64", OptionValueKind.Boolean, defaults.DetectBase64);
        AssertOption(metadata, "formatJson", OptionValueKind.Boolean, defaults.FormatJson);
        AssertOption(metadata, "formatXml", OptionValueKind.Boolean, defaults.FormatXml);
        AssertOption(metadata, "boundedCapacity", OptionValueKind.Number, defaults.BoundedCapacity, 1);
    }

    [Fact]
    public void Design_metadata_provider_describes_payload_option_hints()
    {
        var options = OptionsByName(PayloadDesignMetadata());

        AssertOptionHints(options["maxInputBytes"], "Limits", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(options["maxPreviewBytes"], "Preview", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(options["maxFormattedChars"], "Preview", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(options["detectBase64"], "Detection", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(options["formatJson"], "Formatting", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(options["formatXml"], "Formatting", OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(options["boundedCapacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public void Design_metadata_provider_describes_host_owned_resource_picker_hints()
    {
        var resources = PayloadDesignMetadata().Resources;

        AssertResourceHints(
            resources.Single(resource => resource.Name.Value == PayloadsCompositionResourceNames.Codecs),
            "codec-catalog",
            "Resources.{name}");
        AssertResourceHints(
            resources.Single(resource => resource.Name.Value == PayloadsCompositionResourceNames.Clock),
            ResourceDesignMetadataAttributeValues.Clock,
            "Resources.{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentDesignMetadataCatalog.FromProviders(
            [new PayloadsComponentDesignMetadataProvider()]);

        catalog.All.ShouldHaveSingleItem();
        catalog.TryGet(
            new ComponentType(PayloadsCompositionNodeTypes.Inspect),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull().DisplayName?.Value.ShouldBe("Payload Inspect");
    }

    [Fact]
    public async Task Hosted_payload_inspect_classifies_json_and_preserves_content()
    {
        var content = FlowContent.FromBytes(
            Encoding.UTF8.GetBytes("""{"name":"sample","count":2}"""),
            "application/json");

        var result = await RunNodeAsync(
            content,
            node => node.Configure("maxPreviewBytes", 128));

        result.CorrelationId.ShouldBe(new CorrelationId("payload.inspect"));
        result.Payload.IsError.ShouldBeFalse();
        var inspection = result.Payload.Value.ShouldNotBeNull();
        inspection.Content.ShouldBeSameAs(content);
        inspection.Kind.ShouldBe(PayloadKind.JsonObject);
        inspection.TextPreview.ShouldNotBeNull().ShouldContain("\"name\"");
        inspection.FormattedPreview.ShouldNotBeNull().ShouldContain("\n");
    }

    [Fact]
    public async Task Hosted_payload_inspect_binds_options_from_configuration()
    {
        var result = await RunNodeAsync(
            FlowContent.FromBytes(
                Encoding.UTF8.GetBytes("""{"message":"abcdef"}"""),
                "application/json"),
            node => node
                .Configure("maxPreviewBytes", 3)
                .Configure("maxFormattedChars", 10));

        var inspection = result.Payload.Value.ShouldNotBeNull();
        inspection.TextPreview.ShouldBe("""{"m""");
        inspection.TextPreviewTruncated.ShouldBeTrue();
        inspection.FormattedPreview.ShouldNotBeNull().Length.ShouldBe(10);
        inspection.FormattedPreviewTruncated.ShouldBeTrue();
    }

    [Fact]
    public async Task Hosted_payload_inspect_uses_optional_keyed_resources()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-18T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var codec = new FixedCodec(FlowValue.From("decoded"));
        var catalog = new FlowContentCodecCatalog(
        [
            new(FlowContentCodecMatch.ExactMediaType, "application/example", codec)
        ],
        new BinaryFlowContentCodec());
        var content = FlowContent.FromBytes(new byte[] { 1 }, "application/example");

        var result = await RunNodeAsync(
            content,
            node => node
                .Resource(PayloadsCompositionResourceNames.Codecs, "custom")
                .Resource(PayloadsCompositionResourceNames.Clock, "fixed"),
            services =>
            {
                services.AddKeyedSingleton("custom", catalog);
                services.AddKeyedSingleton<TimeProvider>("fixed", clock);
            });

        result.Payload.Timestamp.ShouldBe(timestamp);
        var inspection = result.Payload.Value.ShouldNotBeNull();
        inspection.Timestamp.ShouldBe(timestamp);
        inspection.DecodedValue.ShouldNotBeNull().GetString().ShouldBe("decoded");
        codec.DecodeCount.ShouldBe(1);
    }

    [Fact]
    public async Task Hosted_payload_inspect_emits_expected_failures_as_results_and_continues()
    {
        await WithNodeAsync(async (input, output, descriptor) =>
        {
            descriptor.Errors.ShouldBeNull();
            var results = Link(output.Source);
            var bad = FlowMessage.Create(
                FlowContent.FromBytes(Encoding.UTF8.GetBytes("{"), "application/json"),
                new CorrelationId("bad"));
            var good = FlowMessage.Create(
                FlowContent.FromBytes(Encoding.UTF8.GetBytes("{}"), "application/json"),
                new CorrelationId("good"));

            (await input.Target.SendAsync(bad).WaitAsync(Timeout)).ShouldBeTrue();
            (await input.Target.SendAsync(good).WaitAsync(Timeout)).ShouldBeTrue();

            var failure = await results.ReceiveAsync().WaitAsync(Timeout);
            var success = await results.ReceiveAsync().WaitAsync(Timeout);
            failure.CorrelationId.ShouldBe(bad.CorrelationId);
            failure.Payload.IsError.ShouldBeTrue();
            failure.Payload.Error!.Code.ShouldBe(PayloadErrorCodeNames.ParseFailed);
            success.CorrelationId.ShouldBe(good.CorrelationId);
            success.Payload.IsError.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Hosted_payload_inspect_exposes_events()
    {
        await WithNodeAsync(async (input, output, descriptor) =>
        {
            output.Source.LinkTo(DataflowBlock.NullTarget<FlowMessage<FlowResult<PayloadInspectionResult>>>());
            var events = Link(descriptor.Events.ShouldNotBeNull());
            var message = FlowMessage.Create(
                FlowContent.FromBytes(Encoding.UTF8.GetBytes("hello"), "text/plain"));

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
            diagnostic.Message.Contains("boundedCapacity", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<FlowMessage<FlowResult<PayloadInspectionResult>>> RunNodeAsync(
        FlowContent content,
        Action<NodeDefinitionBuilder>? configureNode = null,
        Action<IServiceCollection>? configureServices = null)
    {
        FlowMessage<FlowResult<PayloadInspectionResult>>? result = null;
        await WithNodeAsync(
            async (input, output, _) =>
            {
                var results = Link(output.Source);
                var message = FlowMessage.Create(
                    content,
                    new CorrelationId(PayloadsCompositionNodeTypes.Inspect));

                (await input.Target.SendAsync(message).WaitAsync(Timeout)).ShouldBeTrue();
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

    private static Dictionary<string, OptionDesignMetadata> OptionsByName(
        ComponentDesignMetadata metadata)
        => metadata.Options.ToDictionary(option => option.Name.Value, StringComparer.Ordinal);

    private static void AssertOption(
        ComponentDesignMetadata metadata,
        string name,
        OptionValueKind kind,
        object? defaultValue,
        double? min = null)
    {
        var option = metadata.Options.Single(option => option.Name.Value == name);
        option.Kind.ShouldBe(kind);
        option.DefaultValue.ShouldBe(defaultValue);
        option.Min.ShouldBe(min);
    }

    private static void AssertOptionHints(
        OptionDesignMetadata option,
        string section,
        string importance,
        string? editor = null)
    {
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Section)
            .ShouldBe(section);
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Importance)
            .ShouldBe(importance);

        var editorName = new ComponentAttributeName(OptionDesignMetadataAttributeNames.Editor);
        if (editor is null)
            option.Attributes.ContainsKey(editorName).ShouldBeFalse();
        else
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Editor).ShouldBe(editor);

        option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.Syntax))
            .ShouldBeFalse();
        option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.RelatedResource))
            .ShouldBeFalse();
    }

    private static void AssertResources(ComponentDesignMetadata metadata)
    {
        metadata.Resources.Count.ShouldBe(2);
        var codecs = metadata.Resources[0];
        codecs.Name.Value.ShouldBe(PayloadsCompositionResourceNames.Codecs);
        codecs.Order.ShouldBe(0);
        codecs.IsRequired.ShouldBeFalse();
        codecs.ValueType?.Value.ShouldBe(nameof(FlowContentCodecCatalog));

        var clock = metadata.Resources[1];
        clock.Name.Value.ShouldBe(PayloadsCompositionResourceNames.Clock);
        clock.Order.ShouldBe(1);
        clock.IsRequired.ShouldBeFalse();
        clock.ValueType?.Value.ShouldBe(nameof(TimeProvider));
    }

    private static void AssertResourceHints(
        ResourceDesignMetadata resource,
        string pickerKind,
        string keyPattern)
    {
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.Ownership)
            .ShouldBe(ResourceDesignMetadataAttributeValues.HostOwned);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.PickerKind)
            .ShouldBe(pickerKind);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.KeyPattern)
            .ShouldBe(keyPattern);
    }

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;

    private static async Task WithNodeAsync(
        Func<
            CompositionInputPort<FlowContent>,
            CompositionOutputPort<FlowResult<PayloadInspectionResult>>,
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
            .ShouldBeOfType<CompositionInputPort<FlowContent>>();
        var output = descriptor.Outputs[PayloadsCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<FlowResult<PayloadInspectionResult>>>();

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

    private sealed class FixedCodec(FlowValue value) : IFlowContentCodec
    {
        private int _decodeCount;

        public int DecodeCount => Volatile.Read(ref _decodeCount);

        public FlowValue Decode(ImmutableArray<byte> content, string? encoding)
        {
            Interlocked.Increment(ref _decodeCount);
            return value;
        }
    }
}
