using System.Text;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Payloads.Composition;
using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Diagnostics;
using FluxFlow.Components.Payloads.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Payloads.Composition.Tests;

public sealed class PayloadsServiceCollectionExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "node", PayloadsComponentDefinition.Ports.Input);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", PayloadsComponentDefinition.Ports.Output);
    private static readonly ApplicationAddress Events =
        ApplicationAddress.WorkflowPort("main", "node", "Events");

    [Fact]
    public void AddPayloads_registers_canonical_content_result_metadata()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddFluxFlowComponents().AddPayloads());

        var registration = registry.Components[PayloadsComponentDefinition.Types.Inspect];
        registration.Inputs[PayloadsComponentDefinition.Ports.Input].MessageType
            .ShouldBe(typeof(FlowContent));
        registration.Outputs[PayloadsComponentDefinition.Ports.Output].MessageType
            .ShouldBe(typeof(PayloadInspectionResult));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_payload_metadata()
    {
        var metadata = PayloadDesignMetadata();

        metadata.Type.Value.ShouldBe(PayloadsComponentDefinition.Types.Inspect);
        metadata.DisplayName?.Value.ShouldBe("Payload Inspect");
        metadata.Category.ShouldBe(new ComponentCategory("Payloads"));
        metadata.SuggestedEditorWidth.ShouldBe(420);
        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == PayloadsComponentDefinition.Resources.Clock);
        AssertResources(metadata);
    }

    [Fact]
    public void Design_metadata_provider_describes_canonical_payload_ports()
    {
        var metadata = PayloadDesignMetadata();

        metadata.Ports.Count.ShouldBe(3);
        metadata.Ports[^1].Name.Value.ShouldBe("Events");

        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(PayloadsComponentDefinition.Ports.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType?.Value.ShouldBe(nameof(FlowContent));
        input.IsPrimary.ShouldBeTrue();

        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(PayloadsComponentDefinition.Ports.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(1);
        output.ValueType?.Value.ShouldBe("PayloadInspectionResult");
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
            "boundedCapacity",
            "processing"
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
    }

    [Fact]
    public void Design_metadata_provider_describes_host_owned_resource_picker_hints()
    {
        var resources = PayloadDesignMetadata().Resources;

        AssertResourceHints(
            resources.Single(resource => resource.Name.Value == PayloadsComponentDefinition.Resources.Clock),
            ResourceDesignMetadataAttributeValues.Clock,
            "Resources.{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentCatalogTestHost.CreateDesignMetadataCatalog(
            static services => services.AddFluxFlowComponents().AddPayloads());

        catalog.All.ShouldHaveSingleItem();
        catalog.TryGet(
            new ComponentType(PayloadsComponentDefinition.Types.Inspect),
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
            Properties(("maxPreviewBytes", 128)));

        result.CorrelationId.ShouldBe(new CorrelationId("payload.inspect"));
        result.IsError.ShouldBeFalse();
        var inspection = result.Value;
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
            Properties(
                ("maxPreviewBytes", 3),
                ("maxFormattedChars", 10)));

        var inspection = result.Value;
        inspection.TextPreview.ShouldBe("""{"m""");
        inspection.TextPreviewTruncated.ShouldBeTrue();
        inspection.FormattedPreview.ShouldNotBeNull().Length.ShouldBe(10);
        inspection.FormattedPreviewTruncated.ShouldBeTrue();
    }

    [Fact]
    public async Task Hosted_payload_inspect_uses_optional_keyed_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-07-18T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);
        var content = FlowContent.FromBytes(Encoding.UTF8.GetBytes("decoded"), "text/plain");

        var result = await RunNodeAsync(
            content,
            Properties((PayloadsComponentDefinition.Resources.Clock, "Resources.fixed")),
            resources: ["fixed"],
            configureRuntime: context =>
            {
                context.Services.AddExternalFluxFlowResource<TimeProvider>(
                    ApplicationAddress.Resource("fixed"),
                    clock);
            });

        result.Value.Timestamp.ShouldBe(timestamp);
        result.Value.TextPreview.ShouldBe("decoded");
    }

    [Fact]
    public async Task Hosted_payload_inspect_emits_expected_failures_as_results_and_continues()
    {
        await WithNodeAsync(async ports =>
        {
            var bad = FlowMessage.Create(
                FlowContent.FromBytes(Encoding.UTF8.GetBytes("{"), "application/json"),
                new CorrelationId("bad"));
            var good = FlowMessage.Create(
                FlowContent.FromBytes(Encoding.UTF8.GetBytes("{}"), "application/json"),
                new CorrelationId("good"));

            var firstResult = ports.ReceiveAsync<PayloadInspectionResult>(Output, Timeout);
            (await ports.SendAsync(Input, bad)).IsAccepted.ShouldBeTrue();
            var failure = (await firstResult).Message.ShouldNotBeNull();

            var secondResult = ports.ReceiveAsync<PayloadInspectionResult>(Output, Timeout);
            (await ports.SendAsync(Input, good)).IsAccepted.ShouldBeTrue();
            var success = (await secondResult).Message.ShouldNotBeNull();
            failure.CorrelationId.ShouldBe(bad.CorrelationId);
            failure.IsError.ShouldBeTrue();
            failure.Error!.Code.ShouldBe(PayloadErrorCodeNames.ParseFailed);
            success.CorrelationId.ShouldBe(good.CorrelationId);
            success.IsError.ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Hosted_payload_inspect_exposes_events()
    {
        await WithNodeAsync(async ports =>
        {
            var message = FlowMessage.Create(
                FlowContent.FromBytes(Encoding.UTF8.GetBytes("hello"), "text/plain"));

            var eventResult = ports.ReceiveAsync<ComponentEvent>(Events, Timeout);
            (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();

            var eventMessage = (await eventResult).Message.ShouldNotBeNull();
            var @event = eventMessage.Value;
            @event.Name.ShouldBe(PayloadDiagnosticNames.Inspected);
            eventMessage.CorrelationId.ShouldBe(message.CorrelationId);
        });
    }

    [Fact]
    public async Task Invalid_configuration_surfaces_factory_diagnostic()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            CanonicalTestApplication.SingleComponent(
                PayloadsComponentDefinition.Types.Inspect,
                CanonicalTestApplication.Properties(("boundedCapacity", 0))),
            registry => registry.AddFluxFlowComponents().AddPayloads());
        var result = host.StartResult;

        result.Succeeded.ShouldBeFalse();
        result.Update!.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        result.Update.Diagnostics.ShouldContain(failure =>
            failure.Stage == ApplicationUpdateStage.ComponentPreparation &&
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!.Contains(
                "boundedCapacity",
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private static async Task<FlowMessage<PayloadInspectionResult>> RunNodeAsync(
        FlowContent content,
        IReadOnlyDictionary<string, object?>? properties = null,
        IReadOnlyList<string>? resources = null,
        Action<ApplicationResourceRegistrationContext>? configureRuntime = null)
    {
        FlowMessage<PayloadInspectionResult>? result = null;
        await WithNodeAsync(
            async ports =>
            {
                var message = FlowMessage.Create(
                    content,
                    new CorrelationId(PayloadsComponentDefinition.Types.Inspect));

                var receive = ports.ReceiveAsync<PayloadInspectionResult>(
                    Output,
                    Timeout);
                (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();
                result = (await receive).Message.ShouldNotBeNull();
            },
            properties,
            resources,
            configureRuntime);

        return result.ShouldNotBeNull();
    }

    private static ComponentDesignMetadata PayloadDesignMetadata()
        => ComponentCatalogTestHost.CreateDesignMetadataCatalog(
                services => services.AddFluxFlowComponents().AddPayloads()).All
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
        metadata.Resources.Select(resource => resource.Name.Value)
            .ShouldBe([PayloadsComponentDefinition.Resources.Clock, "processing"], ignoreOrder: false);
        var clock = metadata.Resources[0];
        clock.Name.Value.ShouldBe(PayloadsComponentDefinition.Resources.Clock);
        clock.Order.ShouldBe(0);
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
        Func<ApplicationPorts, Task> run,
        IReadOnlyDictionary<string, object?>? properties = null,
        IReadOnlyList<string>? resources = null,
        Action<ApplicationResourceRegistrationContext>? configureRuntime = null)
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            CanonicalTestApplication.SingleComponent(
                PayloadsComponentDefinition.Types.Inspect,
                properties,
                resources),
            registry => registry.AddFluxFlowComponents().AddPayloads(),
            registerResources: configureRuntime);
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts());
    }

}
