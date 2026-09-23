using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;

namespace FluxFlow.Components.ChangeFilter;

/// <summary>Forwards the original message only when its selected key changes.</summary>
public sealed class ChangeFilter<TKey> : FlowTransformNode where TKey : notnull
{
    private readonly Func<FlowMessage, TKey> _select;
    private readonly Func<TKey, TKey, bool> _areEqual;
    private TKey _previous = default!;
    private bool _hasPrevious;

    public ChangeFilter(ChangeFilterOptions<TKey> options)
        : base(Validate(options).Execution, options.OutputCapacity)
        => (_select, _areEqual) = (options.KeySelector, options.AreEqual);

    protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
    {
        var key = _select(message);
        ArgumentNullException.ThrowIfNull(key);
        if (_hasPrevious && _areEqual(_previous, key)) return Task.FromResult<IEnumerable<FlowMessage>>([]);
        _previous = key;
        _hasPrevious = true;
        return Task.FromResult<IEnumerable<FlowMessage>>([message]);
    }

    protected override ValueTask OnDisposeAsync()
    {
        _previous = default!;
        _hasPrevious = false;
        return ValueTask.CompletedTask;
    }

    private static ChangeFilterOptions<TKey> Validate(ChangeFilterOptions<TKey> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.KeySelector);
        ArgumentNullException.ThrowIfNull(options.AreEqual);
        if (options.Execution is { } execution && (execution.MaxDegreeOfParallelism != 1 || !execution.EnsureOrdered))
            throw new ArgumentException("ChangeFilter requires ordered serial processing.", nameof(options));
        return options;
    }
}
