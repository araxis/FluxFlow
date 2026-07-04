namespace FluxFlow.Fluent;

/// <summary>
/// A reusable, named fragment of a flow: given a builder positioned at <typeparamref name="TIn"/>,
/// it appends a fixed sequence of nodes and leaves the builder at <typeparamref name="TOut"/>.
/// Define a segment once and splice it into many flows with <see cref="FlowBuilder{T}.Apply"/>.
/// </summary>
/// <remarks>
/// The segment holds a <em>build</em> delegate, not node instances — each application runs the
/// delegate, which constructs fresh nodes (via <c>Then</c>/<c>Tap</c>/…). Nodes are single-use, so
/// this is what makes a segment safe to reuse across graphs and to apply more than once. Keep the
/// delegate free of captured node instances for the same reason.
/// </remarks>
public sealed class FlowSegment<TIn, TOut>
{
    private readonly Func<FlowBuilder<TIn>, FlowBuilder<TOut>> _build;

    /// <param name="name">A label for the segment, used for readability and diagnostics.</param>
    /// <param name="build">Appends the segment's nodes to a builder and returns the continuation.</param>
    public FlowSegment(string name, Func<FlowBuilder<TIn>, FlowBuilder<TOut>> build)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(build);

        Name = name;
        _build = build;
    }

    /// <summary>The segment's label.</summary>
    public string Name { get; }

    internal FlowBuilder<TOut> ApplyTo(FlowBuilder<TIn> builder)
        => _build(builder)
           ?? throw new InvalidOperationException($"Flow segment '{Name}' returned a null builder.");
}

/// <summary>Factory helpers for <see cref="FlowSegment{TIn,TOut}"/>.</summary>
public static class FlowSegment
{
    /// <summary>Define a reusable named segment from <typeparamref name="TIn"/> to <typeparamref name="TOut"/>.</summary>
    public static FlowSegment<TIn, TOut> Define<TIn, TOut>(
        string name,
        Func<FlowBuilder<TIn>, FlowBuilder<TOut>> build)
        => new(name, build);
}
