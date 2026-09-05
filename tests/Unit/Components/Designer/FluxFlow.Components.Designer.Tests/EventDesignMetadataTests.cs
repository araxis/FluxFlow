using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Composition;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class EventDesignMetadataTests
{
    [Fact]
    public void Valid_event_shape_is_discoverable_on_event_port()
    {
        var shape = new EventDesignMetadata
        {
            Type = "message.received",
            Kind = FlowEventKind.Domain,
            Fields =
            [
                new EventFieldDesignMetadata
                {
                    Path = "attributes.topic",
                    Role = EventFieldRole.Dimension,
                    ValueKind = EventFieldValueKind.String
                },
                new EventFieldDesignMetadata
                {
                    Path = "measurements.bytes",
                    Role = EventFieldRole.Measurement,
                    ValueKind = EventFieldValueKind.Number,
                    Unit = "By"
                }
            ]
        };
        var metadata = CreateMetadata([shape]);

        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Ports[0].EventShapes.ShouldHaveSingleItem().ShouldBe(shape);
    }

    [Fact]
    public void Duplicate_event_types_are_rejected()
    {
        var first = new EventDesignMetadata { Type = "message.received" };
        var second = new EventDesignMetadata { Type = "message.received" };

        var errors = ComponentDesignMetadataValidator.Validate(CreateMetadata([first, second]));

        errors.ShouldContain(error => error.Message.Contains("duplicated", StringComparison.Ordinal));
    }

    private static ComponentDesignMetadata CreateMetadata(IReadOnlyList<EventDesignMetadata> shapes)
        => new()
        {
            Type = new ComponentType("test.event-source"),
            Ports =
            [
                new PortDesignMetadata
                {
                    Name = new ComponentPortName("Events"),
                    Direction = PortDirection.Output,
                    MessageType = typeof(FlowValue),
                    EventShapes = shapes
                }
            ]
        };
}
