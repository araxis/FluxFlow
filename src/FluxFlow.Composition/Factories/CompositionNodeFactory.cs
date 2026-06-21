namespace FluxFlow.Composition;

public delegate ValueTask<ComposedNode> CompositionNodeFactory(CompositionNodeFactoryContext context);
