namespace FluxFlow.Composition;

internal readonly record struct RuntimeNodeKey(string WorkflowName, string ComponentName)
{
    public override string ToString() => $"{WorkflowName}.{ComponentName}";
}
