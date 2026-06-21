namespace FluxFlow.Composition;

public sealed record CompositionReloadRequest(
    CompositionDefinition Current,
    CompositionDefinition Next);
