namespace FluxFlow.Composition;

internal readonly record struct RuntimeNodeKey(string WorkflowName, string NodeName)
{
    public override string ToString() => $"{WorkflowName}.{NodeName}";
}
