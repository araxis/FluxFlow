using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

public sealed class FlowOptionsTests
{
    [Fact]
    public void Flow_node_options_default_to_bounded_serial_processing_and_output()
    {
        var options = new FlowNodeOptions();

        options.InputCapacity.ShouldBe(128);
        options.OutputCapacity.ShouldBe(128);
        options.MaxDegreeOfParallelism.ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Flow_node_options_reject_invalid_input_capacity(int capacity)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new FlowNodeOptions { InputCapacity = capacity });

        exception.ParamName.ShouldBe(nameof(FlowNodeOptions.InputCapacity));
        exception.Message.ShouldContain("InputCapacity");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Flow_node_options_reject_invalid_output_capacity(int capacity)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new FlowNodeOptions { OutputCapacity = capacity });

        exception.ParamName.ShouldBe(nameof(FlowNodeOptions.OutputCapacity));
        exception.Message.ShouldContain("OutputCapacity");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Flow_node_options_reject_invalid_max_degree_of_parallelism(int maxDegreeOfParallelism)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new FlowNodeOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism });

        exception.ParamName.ShouldBe(nameof(FlowNodeOptions.MaxDegreeOfParallelism));
        exception.Message.ShouldContain("MaxDegreeOfParallelism");
    }

    [Fact]
    public void Flow_source_options_default_to_bounded_output()
    {
        var options = new FlowSourceOptions();

        options.OutputCapacity.ShouldBe(128);
    }

    [Fact]
    public void Flow_source_options_allow_positive_output_capacity()
    {
        new FlowSourceOptions { OutputCapacity = 1 }.OutputCapacity.ShouldBe(1);
        new FlowSourceOptions { OutputCapacity = 256 }.OutputCapacity.ShouldBe(256);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-2)]
    public void Flow_source_options_reject_invalid_output_capacity(int capacity)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(
            () => new FlowSourceOptions { OutputCapacity = capacity });

        exception.ParamName.ShouldBe(nameof(FlowSourceOptions.OutputCapacity));
        exception.Message.ShouldContain("OutputCapacity");
    }
}
