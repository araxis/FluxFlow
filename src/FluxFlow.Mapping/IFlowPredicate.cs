namespace FluxFlow.Mapping;

public interface IFlowPredicate<in TInput>
{
    bool IsMatch(TInput input);
}
