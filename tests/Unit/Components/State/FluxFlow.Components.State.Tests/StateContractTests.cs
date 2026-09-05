using System.Collections.Immutable;
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

        var input = new StateReducerInput<string?>
        {
            Key = " state-1 ",
            Input = "payload",
            Variables = variables
        };
        variables["tenant"] = "changed";
        variables["new"] = "value";

        input.Key.ShouldBe("state-1");
        input.Variables.ShouldBeOfType<ImmutableDictionary<string, object?>>()
            .KeyComparer.ShouldBe(StringComparer.Ordinal);
        input.Variables["tenant"].ShouldBe("north");
        input.Variables.ContainsKey("new").ShouldBeFalse();
    }

    [Fact]
    public void Reducer_result_normalizes_key()
    {
        var result = new StateReducerResult<string?>
        {
            Key = " state-1 ",
            Version = 1,
            UpdatedAt = new DateTimeOffset(2026, 6, 26, 12, 0, 0, TimeSpan.Zero)
        };

        result.Key.ShouldBe("state-1");
    }

    [Fact]
    public void Reducer_contracts_preserve_typed_null_members()
    {
        var input = new StateReducerInput<string?>
        {
            Key = null!,
            Input = null!,
            Variables = null!
        };
        var result = new StateReducerResult<string?>
        {
            Key = null!,
            PreviousState = null!,
            Input = null!,
            NewState = null!,
            UpdatedAt = DateTimeOffset.UnixEpoch
        };

        input.Key.ShouldBeEmpty();
        input.Variables.ShouldBeEmpty();
        input.Input.ShouldBeNull();
        result.Key.ShouldBeEmpty();
        result.PreviousState.ShouldBeNull();
        result.Input.ShouldBeNull();
        result.NewState.ShouldBeNull();
    }
}
