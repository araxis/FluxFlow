namespace FluxFlow.Components.Observability.Contracts;

public interface IObservabilityValueSelector<in TInput>
{
    object? Select(TInput input, ObservabilityNodeContext context);
}
