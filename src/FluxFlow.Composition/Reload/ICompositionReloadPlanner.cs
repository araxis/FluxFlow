namespace FluxFlow.Composition;

public interface ICompositionReloadPlanner
{
    CompositionReloadPlan Plan(CompositionReloadRequest request);
}
