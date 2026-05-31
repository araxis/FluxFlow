namespace FluxFlow.Engine.Mapping;

public interface IFlowMapContextFactory<in TInput>
{
    FlowMapContext Create(TInput input);
}
