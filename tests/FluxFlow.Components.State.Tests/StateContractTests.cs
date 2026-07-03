using FluxFlow.Components.State.Contracts;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.State.Tests;

public sealed class StateContractTests
{
    [Fact]
    public void Reducer_input_normalizes_key_and_copies_variables()
    {
        var variables = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["tenant"] = "north"
        };

        var input = new StateReducerInput
        {
            Key = " state-1 ",
            Input = "payload",
            Variables = variables
        };
        variables["tenant"] = "changed";
        variables["new"] = "value";

        input.Key.ShouldBe("state-1");
        input.Variables.Comparer.ShouldBe(StringComparer.Ordinal);
        input.Variables["tenant"].ShouldBe("north");
        input.Variables.ContainsKey("new").ShouldBeFalse();
    }

    [Fact]
    public void Reducer_result_normalizes_key()
    {
        var result = new StateReducerResult
        {
            Key = " state-1 ",
            Version = 1,
            UpdatedAt = new DateTimeOffset(2026, 6, 26, 12, 0, 0, TimeSpan.Zero)
        };

        result.Key.ShouldBe("state-1");
    }

    [Fact]
    public void Contracts_treat_null_key_as_empty_and_null_variables_as_empty()
    {
        var input = new StateReducerInput
        {
            Key = null!,
            Variables = null!
        };
        var result = new StateReducerResult
        {
            Key = null!,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };

        input.Key.ShouldBeEmpty();
        input.Variables.ShouldBeEmpty();
        input.Variables.Comparer.ShouldBe(StringComparer.Ordinal);
        result.Key.ShouldBeEmpty();
    }
}
