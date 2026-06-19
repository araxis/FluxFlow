namespace FluxFlow.Mapping;

public interface IFlowMapper<in TInput, out TOutput>
{
    TOutput Map(TInput input, FlowMapContext context);
}
