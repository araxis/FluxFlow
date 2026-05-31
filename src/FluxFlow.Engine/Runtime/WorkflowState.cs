namespace FluxFlow.Engine.Runtime;

public enum WorkflowState
{
    Idle,
    Starting,
    Running,
    Stopping,
    Stopped,
    Faulted
}
