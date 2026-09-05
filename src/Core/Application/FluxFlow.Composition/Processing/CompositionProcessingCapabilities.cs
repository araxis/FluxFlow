namespace FluxFlow.Composition;

[Flags]
public enum CompositionProcessingCapabilities
{
    Sequential = 0,
    ParallelPreservingOrder = 1,
    ParallelRelaxedOrder = 2
}
