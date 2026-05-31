namespace FluxFlow.Engine.Mapping;

public sealed class DelegateFlowPredicate<TInput> : IFlowPredicate<TInput>
{
    private readonly Func<TInput, bool> _predicate;

    public DelegateFlowPredicate(Func<TInput, bool> predicate)
    {
        _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
    }

    public bool IsMatch(TInput input) => _predicate(input);
}
