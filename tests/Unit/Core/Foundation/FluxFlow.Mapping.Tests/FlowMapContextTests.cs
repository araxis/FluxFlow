using Shouldly;
using Xunit;

namespace FluxFlow.Mapping.Tests;

public sealed class FlowMapContextTests
{
    [Fact]
    public void Variables_AreCopiedOnAssignment()
    {
        var source = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["input"] = "first"
        };

        var context = new FlowMapContext
        {
            Variables = source
        };

        source["input"] = "changed";
        source["new"] = "later";

        context.Variables["input"].ShouldBe("first");
        context.Variables.ContainsKey("new").ShouldBeFalse();
    }

    [Fact]
    public void Variables_UseOrdinalKeysAfterAssignment()
    {
        var context = new FlowMapContext
        {
            Variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Input"] = 42
            }
        };

        context.Variables.ContainsKey("Input").ShouldBeTrue();
        context.Variables.ContainsKey("input").ShouldBeFalse();
    }

    [Fact]
    public void Variables_TreatNullAssignmentAsEmpty()
    {
        var context = new FlowMapContext
        {
            Variables = null!
        };

        context.Variables.ShouldBeEmpty();
    }
}
