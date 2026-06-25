using FluxFlow.Components.State.Options;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.State.Tests;

public sealed class StateOptionsTests
{
    [Fact]
    public void Reducer_options_normalize_text_values()
    {
        var options = new StateReducerOptions
        {
            Engine = " diagnostic ",
            KeyExpression = " topic ",
            Reducer = " count ",
            ExpressionId = " expression-1 ",
            ExpressionName = " counter ",
            BoundedCapacity = 4,
            MaxKeys = 2
        };

        options.Engine.ShouldBe("diagnostic");
        options.KeyExpression.ShouldBe("topic");
        options.Reducer.ShouldBe("count");
        options.ExpressionId.ShouldBe("expression-1");
        options.ExpressionName.ShouldBe("counter");
        options.BoundedCapacity.ShouldBe(4);
        options.MaxKeys.ShouldBe(2);
    }

    [Fact]
    public void Reducer_options_treat_blank_optional_text_as_absent()
    {
        var options = new StateReducerOptions
        {
            Engine = " ",
            Reducer = " count ",
            ExpressionId = "\t",
            ExpressionName = "\r\n"
        };

        options.Engine.ShouldBeNull();
        options.ExpressionId.ShouldBeNull();
        options.ExpressionName.ShouldBeNull();
    }

    [Fact]
    public void Reducer_options_reject_invalid_values()
    {
        Should.Throw<ArgumentException>(
            () => new StateReducerOptions { Reducer = " " })
            .Message.ShouldContain("reducer");
        Should.Throw<ArgumentException>(
            () => new StateReducerOptions
            {
                Reducer = "count",
                KeyExpression = " "
            })
            .Message.ShouldContain("keyExpression");
        Should.Throw<ArgumentOutOfRangeException>(
            () => new StateReducerOptions
            {
                Reducer = "count",
                BoundedCapacity = 0
            })
            .Message.ShouldContain("boundedCapacity");
        Should.Throw<ArgumentOutOfRangeException>(
            () => new StateReducerOptions
            {
                Reducer = "count",
                MaxKeys = -1
            })
            .Message.ShouldContain("maxKeys");
    }
}
