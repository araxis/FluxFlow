namespace FluxFlow.Application.Addressing;

/// <summary>A validated, full workflow.component address.</summary>
public sealed record ComponentAddress
{
    private readonly ApplicationAddress _address;

    internal ComponentAddress(ApplicationAddress address)
    {
        if (address.Kind != ApplicationAddressKind.WorkflowComponent)
            throw new FormatException("A component address must use 'Workflow.Component'.");
        _address = address;
    }

    public string Value => _address.Value;
    public string WorkflowName => _address.WorkflowName!;
    public string ComponentName => _address.ComponentName!;

    public static ComponentAddress Parse(string value) => new(ApplicationAddress.Parse(value));
    public static ComponentAddress Create(string workflow, string component)
        => new(ApplicationAddress.WorkflowComponent(workflow, component));

    public PortAddress Port(string name) => PortAddress.Create(this, name);
    public override string ToString() => Value;
}
