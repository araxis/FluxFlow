namespace FluxFlow.Composition;

public delegate ValueTask<ComponentInstance> ComponentFactory(ComponentActivationContext context);
