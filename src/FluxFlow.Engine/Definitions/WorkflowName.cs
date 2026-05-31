namespace FluxFlow.Engine.Definitions;

public readonly record struct WorkflowName
{
    public WorkflowName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Workflow name cannot be empty.", nameof(value));

        if (value.Contains('.'))
            throw new ArgumentException("Workflow name cannot contain '.'.", nameof(value));

        Value = value;
    }

    public string Value { get; }
    public override string ToString() => Value;
}
