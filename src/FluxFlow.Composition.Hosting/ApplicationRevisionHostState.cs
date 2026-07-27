namespace FluxFlow.Composition.Hosting;

[Obsolete("Use ApplicationState from FluxFlow.Engine.")]
public enum ApplicationRevisionHostState
{
    Empty = 1,
    Starting = 2,
    Running = 3,
    Degraded = 4,
    Stopped = 5,
    Disposed = 6
}
