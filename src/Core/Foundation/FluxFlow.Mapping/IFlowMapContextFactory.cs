namespace FluxFlow.Mapping;

public interface IFlowMapContextFactory<in TInput>
{
    FlowMapContext Create(TInput input);
}
