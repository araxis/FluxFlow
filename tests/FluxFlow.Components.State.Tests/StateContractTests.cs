using System.Collections.Immutable;
using FluxFlow.Components.State.Contracts;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.State.Tests;

public sealed class StateContractTests
{
    [Fact]
    public void Flow_value_reducer_input_normalizes_key_and_copies_variables()
    {
        var variables = new Dictionary<string, FlowValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["tenant"] = FlowValue.From("north")
        };

        var input = new FlowValueStateReducerInput
        {
            Key = " state-1 ",
            Input = FlowValue.From("payload"),
            Variables = variables
        };
        variables["tenant"] = FlowValue.From("changed");
        variables["new"] = FlowValue.From("value");

        input.Key.ShouldBe("state-1");
        input.Variables.ShouldBeOfType<ImmutableDictionary<string, FlowValue>>()
            .KeyComparer.ShouldBe(StringComparer.Ordinal);
        input.Variables["tenant"].GetString().ShouldBe("north");
        input.Variables.ContainsKey("new").ShouldBeFalse();
    }

    [Fact]
    public void Flow_value_reducer_result_normalizes_key()
    {
        var result = new FlowValueStateReducerResult
        {
            Key = " state-1 ",
            Version = 1,
            UpdatedAt = new DateTimeOffset(2026, 6, 26, 12, 0, 0, TimeSpan.Zero)
        };

        result.Key.ShouldBe("state-1");
    }

    [Fact]
    public void Flow_value_contracts_normalize_null_members()
    {
        var input = new FlowValueStateReducerInput
        {
            Key = null!,
            Input = null!,
            Variables = null!
        };
        var result = new FlowValueStateReducerResult
        {
            Key = null!,
            PreviousState = null!,
            Input = null!,
            NewState = null!,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };

        input.Key.ShouldBeEmpty();
        input.Variables.ShouldBeEmpty();
        input.Input.ShouldBeSameAs(FlowValue.Null);
        result.Key.ShouldBeEmpty();
        result.PreviousState.ShouldBeSameAs(FlowValue.Null);
        result.Input.ShouldBeSameAs(FlowValue.Null);
        result.NewState.ShouldBeSameAs(FlowValue.Null);
    }
}
