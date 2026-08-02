namespace FluxFlow.Engine;

public enum ApplicationState
{
    Empty = 1,
    Starting = 2,
    Running = 3,
    Reloading = 4,
    Degraded = 5,
    Stopping = 6,
    Stopped = 7
}
