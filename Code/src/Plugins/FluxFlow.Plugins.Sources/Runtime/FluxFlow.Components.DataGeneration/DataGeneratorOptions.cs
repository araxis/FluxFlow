using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.DataGeneration;

public sealed class DataGeneratorOptions
{
    public required string Expression { get; init; }
    public string Locale { get; init; } = "en";
    /// <summary>Native input options. Generation processes messages sequentially.</summary>
    public ExecutionDataflowBlockOptions? Execution { get; init; }
    public int OutputCapacity { get; init; } = 128;
}
