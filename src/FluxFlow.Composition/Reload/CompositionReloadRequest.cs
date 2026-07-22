namespace FluxFlow.Composition;

#pragma warning disable CS0618 // Compatibility request intentionally carries legacy definitions.
public sealed record CompositionReloadRequest(
    CompositionDefinition Current,
    CompositionDefinition Next);
#pragma warning restore CS0618
