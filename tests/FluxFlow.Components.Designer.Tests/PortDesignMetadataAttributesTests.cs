using FluxFlow.Components.Designer.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Designer.Tests;

public sealed class PortDesignMetadataAttributesTests
{
    [Fact]
    public void CreateSignal_describes_a_payload_independent_input()
    {
        var attributes = PortDesignMetadataAttributes.CreateSignal();

        attributes.Count.ShouldBe(1);
        attributes[PortDesignMetadataAttributeNames.Kind]
            .ShouldBe(PortDesignMetadataAttributeValues.Signal);
        PortDesignMetadataAttributeValues.Message.ShouldBe("message");
    }
}
