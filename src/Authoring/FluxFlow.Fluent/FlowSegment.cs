namespace FluxFlow.Fluent;

/// <summary>A reusable named construction step over the same canonical graph.</summary>
/// <remarks>Construct fresh component instances inside the delegate; do not capture single-use nodes.</remarks>
public sealed class FlowSegment
{
    private readonly Func<FlowBuilder, FlowBuilder> _build;

    public FlowSegment(string name, Func<FlowBuilder, FlowBuilder> build)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(build);
        Name = name;
        _build = build;
    }

    public string Name { get; }

    public static FlowSegment Define(string name, Func<FlowBuilder, FlowBuilder> build) => new(name, build);

    internal FlowBuilder ApplyTo(FlowBuilder builder)
    {
        var result = _build(builder)
            ?? throw new InvalidOperationException($"Flow segment '{Name}' returned a null builder.");
        if (!builder.SharesGraph(result))
            throw new InvalidOperationException($"Flow segment '{Name}' returned a builder from another graph.");
        return result;
    }
}
