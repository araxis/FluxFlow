using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Projections;
using FluxFlow.Components.Projections.Composition;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Components.Projections.Diagnostics;
using FluxFlow.Components.Projections.Options;
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

namespace FluxFlow.Components.Projections.Composition.Tests;

public sealed class ProjectionsServiceCollectionExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "node", ProjectionsComponentPortNames.Input);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", ProjectionsComponentPortNames.Output);
    private static readonly ApplicationAddress Events =
        ApplicationAddress.WorkflowPort("main", "node", ComponentEvents.PortName);

    [Fact]
    public void AddProjectionsComponents_registers_request_result_metadata()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddProjectionsComponents());

        var registration = registry.Components[ProjectionsComponentTypes.EventProjection];
        registration.Inputs[ProjectionsComponentPortNames.Input].MessageType
            .ShouldBe(typeof(ProjectionEvent));
        registration.Outputs[ProjectionsComponentPortNames.Output].MessageType
            .ShouldBe(typeof(EventProjectionSnapshot));
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_projection_metadata()
    {
        var metadata = ProjectionDesignMetadata();

        metadata.Type.Value.ShouldBe(ProjectionsComponentTypes.EventProjection);
        metadata.DisplayName?.Value.ShouldBe("Event Projection");
        metadata.Category.ShouldBe(new ComponentCategory("Projections"));
        metadata.SuggestedEditorWidth.ShouldBe(460);
        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Options.ShouldNotContain(option =>
            option.Name.Value == ProjectionsComponentResourceNames.Clock);
        AssertClockResource(metadata);
    }

    [Fact]
    public void Design_metadata_provider_describes_projection_ports()
    {
        var metadata = ProjectionDesignMetadata();

        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(ProjectionsComponentPortNames.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType?.Value.ShouldBe(nameof(ProjectionEvent));
        input.IsPrimary.ShouldBeTrue();

        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(ProjectionsComponentPortNames.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(1);
        output.ValueType?.Value.ShouldBe("EventProjectionSnapshot");
        output.IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public void Design_metadata_provider_describes_projection_options()
    {
        var metadata = ProjectionDesignMetadata();
        var defaults = new EventProjectionOptions();

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "name",
            "filter",
            "rateWindowSeconds",
            "emitEveryMatch",
            "emitFinalSnapshot",
            "maxPreviewChars",
            "boundedCapacity"
        ], ignoreOrder: false);

        AssertOption(metadata, "name", OptionValueKind.Text, defaultValue: null);

        var filter = metadata.Options.Single(option => option.Name.Value == "filter");
        filter.Kind.ShouldBe(OptionValueKind.Json);
        filter.DefaultValue.ShouldBeOfType<EventFilter>();

        AssertOption(
            metadata,
            "rateWindowSeconds",
            OptionValueKind.Number,
            defaults.RateWindowSeconds,
            min: 0.000001);
        AssertOption(
            metadata,
            "emitEveryMatch",
            OptionValueKind.Boolean,
            defaults.EmitEveryMatch);
        AssertOption(
            metadata,
            "emitFinalSnapshot",
            OptionValueKind.Boolean,
            defaults.EmitFinalSnapshot);
        AssertOption(
            metadata,
            "maxPreviewChars",
            OptionValueKind.Number,
            defaults.MaxPreviewChars,
            min: 0);
        AssertOption(
            metadata,
            "boundedCapacity",
            OptionValueKind.Number,
            defaults.BoundedCapacity,
            min: 1);
    }

    [Fact]
    public void Design_metadata_provider_describes_projection_option_hints()
    {
        var metadata = ProjectionDesignMetadata();
        var options = OptionsByName(metadata);

        AssertOptionHints(
            options["name"],
            "Diagnostics",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Text);
        AssertOptionHints(
            options["filter"],
            "Filtering",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Json);
        AssertOptionHints(
            options["rateWindowSeconds"],
            "Rate",
            OptionDesignMetadataAttributeValues.Primary,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(
            options["emitEveryMatch"],
            "Emission",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(
            options["emitFinalSnapshot"],
            "Emission",
            OptionDesignMetadataAttributeValues.Advanced);
        AssertOptionHints(
            options["maxPreviewChars"],
            "Preview",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
        AssertOptionHints(
            options["boundedCapacity"],
            "Runtime",
            OptionDesignMetadataAttributeValues.Advanced,
            OptionDesignMetadataAttributeValues.Number);
    }

    [Fact]
    public void Design_metadata_provider_describes_projection_resource_picker_hints()
    {
        var metadata = ProjectionDesignMetadata();

        AssertResourceHints(
            metadata.Resources.ShouldHaveSingleItem(),
            ResourceDesignMetadataAttributeValues.Clock,
            "clock:{name}");
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var catalog = ComponentCatalogTestHost.CreateDesignMetadataCatalog(
            static services => services.AddProjectionsComponents());

        catalog.All.ShouldHaveSingleItem();
        catalog.TryGet(
            new ComponentType(ProjectionsComponentTypes.EventProjection),
            out var metadata).ShouldBeTrue();
        metadata.ShouldNotBeNull()
            .DisplayName?.Value.ShouldBe("Event Projection");
    }

    [Fact]
    public async Task Hosted_event_projection_filters_events_and_preserves_correlation_id()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);

        await WithNodeAsync(
            async (ports, _) =>
            {
                var first = FlowMessage.Create(
                    CreateEvent(
                        timestamp.AddSeconds(-10),
                        "operation.completed",
                        subject: "orders/1",
                        status: "failed",
                        payloadPreview: "abcdef",
                        attributes: new Dictionary<string, string>
                        {
                            ["tenant"] = "north"
                        }),
                    new CorrelationId("first"));
                var ignored = FlowMessage.Create(CreateEvent(
                    timestamp.AddSeconds(-5),
                    "operation.completed",
                    subject: "orders/2",
                    status: "ok",
                    attributes: new Dictionary<string, string>
                    {
                        ["tenant"] = "north"
                    }));
                var second = FlowMessage.Create(
                    CreateEvent(
                        timestamp.AddSeconds(-1),
                        "operation.completed",
                        subject: "orders/3",
                        status: "failed",
                        payloadPreview: "xyz",
                        attributes: new Dictionary<string, string>
                        {
                            ["tenant"] = "north"
                        }),
                    new CorrelationId("second"));

                var firstReceive = ports.ReceiveAsync<EventProjectionSnapshot>(Output, Timeout);
                (await ports.SendAsync(Input, first)).IsAccepted.ShouldBeTrue();
                var firstSnapshot = (await firstReceive).Message.ShouldNotBeNull();
                (await ports.SendAsync(Input, ignored)).IsAccepted.ShouldBeTrue();
                var secondReceive = ports.ReceiveAsync<EventProjectionSnapshot>(Output, Timeout);
                (await ports.SendAsync(Input, second)).IsAccepted.ShouldBeTrue();
                var secondSnapshot = (await secondReceive).Message.ShouldNotBeNull();

                firstSnapshot.CorrelationId.ShouldBe(first.CorrelationId);
                firstSnapshot.IsError.ShouldBeFalse();
                var firstValue = firstSnapshot.Value;
                firstValue.Timestamp.ShouldBe(timestamp);
                firstValue.Name.ShouldBe("errors");
                firstValue.ObservedCount.ShouldBe(1);
                firstValue.MatchedCount.ShouldBe(1);
                firstValue.Latest.ShouldNotBeNull().PayloadPreview.ShouldBe("abcd");

                secondSnapshot.CorrelationId.ShouldBe(second.CorrelationId);
                var secondValue = secondSnapshot.Value;
                secondValue.ObservedCount.ShouldBe(3);
                secondValue.MatchedCount.ShouldBe(2);
                secondValue.CurrentRate.ShouldBe(0.2d);
                secondValue.Latest.ShouldNotBeNull().Subject.ShouldBe("orders/3");
            },
            Properties(
                ("name", "errors"),
                ("rateWindowSeconds", 10),
                ("maxPreviewChars", 4),
                ("filter", new EventFilter
                    {
                        Type = "operation.completed",
                        SubjectPrefix = "orders/",
                        Status = "failed",
                        Attributes = new Dictionary<string, string>
                        {
                            ["tenant"] = "north"
                        }
                    }),
                (ProjectionsComponentResourceNames.Clock, "Resources.fixed")),
            resources: ["fixed"],
            configureRuntime: context => context.Services.AddExternalFluxFlowResource<TimeProvider>(
                ApplicationAddress.Resource("fixed"),
                clock));
    }

    [Fact]
    public async Task Hosted_event_projection_binds_nested_filter_configuration()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:30:00Z");
        await WithNodeAsync(
            async (ports, _) =>
            {
                var receive = ports.ReceiveAsync<EventProjectionSnapshot>(Output, Timeout);
                (await ports.SendAsync(Input, FlowMessage.Create(CreateEvent(
                        timestamp,
                        "task.completed",
                        source: "worker",
                        subject: "jobs/42",
                        status: "failed",
                        attributes: new Dictionary<string, string>
                        {
                            ["tenant"] = "north"
                        })))).IsAccepted.ShouldBeTrue();

                var snapshot = (await receive).Message.ShouldNotBeNull();
                var value = snapshot.Value;
                value.MatchedCount.ShouldBe(1);
                value.Filter.TypePrefix.ShouldBe("task.");
                value.Filter.Status.ShouldBe("failed");
                value.Filter.SubjectPrefix.ShouldBe("jobs/");
                value.Filter.Attributes["tenant"].ShouldBe("north");
            },
            Properties(("filter", new EventFilter
                {
                    TypePrefix = "task.",
                    SubjectPrefix = "jobs/",
                    Status = "failed",
                    Attributes = new Dictionary<string, string>
                    {
                        ["tenant"] = "north"
                    }
                })));
    }

    [Fact]
    public async Task Hosted_event_projection_exposes_events()
    {
        await WithNodeAsync(async (ports, _) =>
        {
            var message = FlowMessage.Create(CreateEvent(
                DateTimeOffset.Parse("2026-06-18T13:00:00Z"),
                "event.created"));

            var eventReceive = ports.ReceiveAsync<ComponentEvent>(Events, Timeout);
            (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();

            var eventMessage = (await eventReceive).Message.ShouldNotBeNull();
            var @event = eventMessage.Value;
            @event.Name.ShouldBe(ProjectionDiagnosticNames.ProjectionUpdated);
            eventMessage.CorrelationId.ShouldBe(message.CorrelationId);
            @event.Attributes["matchedCount"].ShouldBe("1");
        });
    }

    [Fact]
    public async Task Hosted_event_projection_propagates_error_and_continues()
    {
        await WithNodeAsync(async (ports, _) =>
        {
            var upstreamError = new FlowError(
                "upstream.failed",
                "Projection input was unavailable.",
                "Projections",
                isTransient: false);
            var missing = FlowMessage.CreateError<ProjectionEvent>(
                upstreamError,
                new CorrelationId("missing"));
            var valid = FlowMessage.Create(
                CreateEvent(
                    DateTimeOffset.Parse("2026-06-18T13:05:00Z"),
                    "event.created"),
                new CorrelationId("valid"));

            var failureReceive = ports.ReceiveAsync<EventProjectionSnapshot>(Output, Timeout);
            (await ports.SendAsync(Input, missing)).IsAccepted.ShouldBeTrue();
            var failure = (await failureReceive).Message.ShouldNotBeNull();
            var successReceive = ports.ReceiveAsync<EventProjectionSnapshot>(Output, Timeout);
            (await ports.SendAsync(Input, valid)).IsAccepted.ShouldBeTrue();
            var success = (await successReceive).Message.ShouldNotBeNull();
            failure.CorrelationId.ShouldBe(missing.CorrelationId);
            failure.IsError.ShouldBeTrue();
            failure.Error.ShouldBeSameAs(upstreamError);
            success.CorrelationId.ShouldBe(valid.CorrelationId);
            success.Value.MatchedCount.ShouldBe(1);
        });
    }

    [Fact]
    public async Task Invalid_configuration_surfaces_factory_diagnostic()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                ProjectionsComponentTypes.EventProjection,
                Properties(("rateWindowSeconds", 0))),
            registry => registry.AddProjectionsComponents());

        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        host.StartResult.Update.Diagnostics.ShouldContain(failure =>
            failure.Stage == ApplicationUpdateStage.ComponentPreparation &&
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!.Contains(
                "rateWindowSeconds",
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private static ComponentDesignMetadata ProjectionDesignMetadata()
        => new ProjectionsComponentDesignMetadataProvider()
            .GetMetadata()
            .ShouldHaveSingleItem();

    private static Dictionary<string, OptionDesignMetadata> OptionsByName(
        ComponentDesignMetadata metadata)
        => metadata.Options.ToDictionary(
            option => option.Name.Value,
            StringComparer.Ordinal);

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

        if (editor is null)
        {
            option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.Editor))
                .ShouldBeFalse();
        }
        else
        {
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Editor)
                .ShouldBe(editor);
        }

        option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.Syntax))
            .ShouldBeFalse();
        option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.RelatedResource))
            .ShouldBeFalse();
    }

    private static void AssertClockResource(ComponentDesignMetadata metadata)
    {
        var resource = metadata.Resources.ShouldHaveSingleItem();

        resource.Name.Value.ShouldBe(ProjectionsComponentResourceNames.Clock);
        resource.DisplayName?.Value.ShouldBe("Clock");
        resource.Order.ShouldBe(0);
        resource.IsRequired.ShouldBeFalse();
        resource.ValueType?.Value.ShouldBe(nameof(TimeProvider));
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
        Func<ApplicationPorts, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?>? properties = null,
        IReadOnlyList<string>? resources = null,
        Action<ApplicationResourceRegistrationContext>? configureRuntime = null)
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                ProjectionsComponentTypes.EventProjection,
                properties,
                resources),
            registry => registry.AddProjectionsComponents(),
            registerResources: configureRuntime);
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
    }

    private static ProjectionEvent CreateEvent(
        DateTimeOffset timestamp,
        string type,
        string source = "processor",
        string? subject = null,
        string? status = null,
        string? channel = null,
        string? payloadPreview = null,
        string? sourceNodeId = null,
        IReadOnlyDictionary<string, string>? attributes = null)
        => new()
        {
            Timestamp = timestamp,
            Type = type,
            Source = source,
            SourceNodeId = sourceNodeId,
            Subject = subject,
            Status = status,
            Channel = channel,
            PayloadBytes = payloadPreview?.Length,
            PayloadPreview = payloadPreview,
            Attributes = attributes ?? new Dictionary<string, string>(StringComparer.Ordinal)
        };
}
