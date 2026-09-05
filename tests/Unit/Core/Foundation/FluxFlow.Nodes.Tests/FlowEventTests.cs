using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowEventTests
{
    [Fact]
    public void Attributes_AreCopiedOnAssignment()
    {
        var attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["kind"] = "original"
        };

        var flowEvent = new FlowEvent
        {
            Name = "node.event",
            Attributes = attributes
        };

        attributes["kind"] = "changed";
        attributes["new"] = "later";

        flowEvent.Attributes["kind"].ShouldBe("original");
        flowEvent.Attributes.ContainsKey("new").ShouldBeFalse();
    }

    [Fact]
    public void Attributes_UseOrdinalKeysAfterAssignment()
    {
        var flowEvent = new FlowEvent
        {
            Name = "node.event",
            Attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Kind"] = "diagnostic"
            }
        };

        flowEvent.Attributes.ContainsKey("Kind").ShouldBeTrue();
        flowEvent.Attributes.ContainsKey("kind").ShouldBeFalse();
    }
}
