namespace FluxFlow.Mapping;

public sealed class DelegateFlowMapper<TInput, TOutput> : IFlowMapper<TInput, TOutput>
{
    private readonly Func<TInput, FlowMapContext, TOutput> _map;

    public DelegateFlowMapper(Func<TInput, TOutput> map)
        : this((input, _) => map(input))
    {
        ArgumentNullException.ThrowIfNull(map);
    }

    public DelegateFlowMapper(Func<TInput, FlowMapContext, TOutput> map)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
    }

    public TOutput Map(TInput input, FlowMapContext context) => _map(input, context);
}
