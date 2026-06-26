using FluxFlow.Components.Journal;
using FluxFlow.Components.Journal.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Journal.Tests;

public sealed class JournalEventInputBuilderTests
{
    [Fact]
    public void Builder_creates_normalized_event_input()
    {
        var attributes = new Dictionary<string, string>
        {
            [" tenant "] = " primary "
        };

        var input = JournalEventInputBuilder
            .Create(Timestamp(1))
            .WithType(" item.accepted ")
            .WithStatus(" ok ")
            .WithSource(" worker ")
            .WithSourceNodeId(" node-1 ")
            .WithSubject(" item/42 ")
            .WithChannel(" events/items ")
            .WithPayload(bytes: 12, preview: " accepted ")
            .AddAttributes(attributes)
            .BuildInput();

        attributes["tenant"] = "changed";

        input.Timestamp.ShouldBe(Timestamp(1));
        input.Type.ShouldBe("item.accepted");
        input.Status.ShouldBe("ok");
        input.Source.ShouldBe("worker");
        input.SourceNodeId.ShouldBe("node-1");
        input.Subject.ShouldBe("item/42");
        input.Channel.ShouldBe("events/items");
        input.PayloadBytes.ShouldBe(12);
        input.PayloadPreview.ShouldBe("accepted");
        input.Attributes["tenant"].ShouldBe("primary");
    }

    [Fact]
    public void Builder_maps_reserved_attributes_to_record()
    {
        var record = JournalEventInputBuilder
            .Create(Timestamp(2))
            .WithType("item.accepted")
            .WithStatus("ok")
            .WithSource("worker")
            .WithSubject("item/42")
            .WithChannel("events/items")
            .WithPayloadPreview("fallback summary")
            .WithWorkflow(" wf-1 ", " main ")
            .WithNode(" node-from-attribute ")
            .WithComponent(" component-1 ")
            .WithSeverity(" info ")
            .WithLevel(" low ")
            .WithSummary(" accepted ")
            .AddAttribute("tenant", "primary")
            .BuildRecord(" evt-1 ");

        record.Id.ShouldBe("evt-1");
        record.Timestamp.ShouldBe(Timestamp(2));
        record.WorkflowId.ShouldBe("wf-1");
        record.WorkflowName.ShouldBe("main");
        record.NodeId.ShouldBe("node-from-attribute");
        record.ComponentId.ShouldBe("component-1");
        record.Severity.ShouldBe("info");
        record.Level.ShouldBe("low");
        record.Summary.ShouldBe("accepted");
        record.PayloadPreview.ShouldBe("fallback summary");
        record.Attributes["tenant"].ShouldBe("primary");
    }

    [Fact]
    public void Builder_source_node_id_takes_precedence_over_node_attribute()
    {
        var record = JournalEventInputBuilder
            .Create(Timestamp(3))
            .WithSourceNodeId(" source-node ")
            .WithNode(" attribute-node ")
            .BuildRecord("evt-1");

        record.NodeId.ShouldBe("source-node");
    }

    [Fact]
    public void Builder_snapshots_attributes_when_input_is_built()
    {
        var builder = JournalEventInputBuilder
            .Create(Timestamp(1))
            .AddAttribute("tenant", "primary");

        var input = builder.BuildInput();

        builder.AddAttribute("region", "eu");

        input.Attributes.Keys.ShouldBe(["tenant"]);
    }

    [Fact]
    public void Builder_requires_timestamp()
    {
        Should.Throw<InvalidOperationException>(() =>
                new JournalEventInputBuilder().BuildInput())
            .Message.ShouldContain("timestamp");

        Should.Throw<ArgumentException>(() =>
            JournalEventInputBuilder.Create(default));
    }

    [Fact]
    public void Builder_rejects_null_attribute_ranges_and_values()
    {
        var builder = JournalEventInputBuilder.Create(Timestamp(1));

        Should.Throw<ArgumentNullException>(() =>
            builder.AddAttributes(null!));
        Should.Throw<ArgumentNullException>(() =>
            builder.AddAttribute(null!, "value"));
        Should.Throw<ArgumentNullException>(() =>
            builder.AddAttribute("name", null!));
    }

    [Fact]
    public void Builder_uses_existing_attribute_validation()
    {
        var builder = JournalEventInputBuilder
            .Create(Timestamp(1))
            .AddAttribute(" tenant ", "primary")
            .AddAttribute("tenant", "secondary");

        Should.Throw<ArgumentException>(() => builder.BuildInput())
            .Message.ShouldContain("declared more than once");
    }

    private static DateTimeOffset Timestamp(int minute)
        => new(2026, 1, 1, 0, minute, 0, TimeSpan.Zero);
}
