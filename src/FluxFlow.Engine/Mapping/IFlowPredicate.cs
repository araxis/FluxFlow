namespace FluxFlow.Engine.Mapping;

public interface IFlowPredicate<in TInput>
{
    bool IsMatch(TInput input);
}
