using FluxFlow.Core.Mapping;
using Shouldly;
using Xunit;

namespace FluxFlow.Mapping.Tests;

public sealed class FlowMapContextTests
{
    [Fact]
    public void Message_conditions_keep_the_original_envelope_and_lazy_payload()
    {
        FluxFlow.Core.Messaging.FlowValue value = new UnserializableValue();
        var message = FluxFlow.Core.Messaging.FlowMessage.Create(value);
        var context = FlowMapContext.ForMessage(message);

        context.Variables["input"].ShouldBeSameAs(value);
        context.Variables["payload"].ShouldBeSameAs(value);
        context.Variables["message"].ShouldBeSameAs(message);
        context.Variables.ContainsKey("Input").ShouldBeFalse();
    }

    [Fact]
    public void Condition_variables_are_isolated_between_evaluations()
    {
        FluxFlow.Core.Messaging.FlowValue value = new UnserializableValue();
        var message = FluxFlow.Core.Messaging.FlowMessage.Create(value);
        var first = FlowMapContext.ForMessage(message);
        var second = FlowMapContext.ForMessage(message);
        ((IDictionary<string, object?>)first.Variables)["input"] = "local override";

        second.Variables["input"].ShouldBeSameAs(value);
        FlowMapContext.ForMessage(message).Variables["input"].ShouldBeSameAs(value);
        message.Value.ShouldBeSameAs(value);
    }

    private sealed class UnserializableValue : FluxFlow.Core.Messaging.FlowValue
    {
        protected override System.Text.Json.JsonElement CreateJson()
            => throw new InvalidOperationException("Condition context must not materialize JSON.");
    }

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
