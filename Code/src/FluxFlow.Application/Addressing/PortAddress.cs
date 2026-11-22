namespace FluxFlow.Application.Addressing;

/// <summary>A validated, full workflow or system port address. Direction is determined by its registered block.</summary>
public sealed record PortAddress
{
    private readonly ApplicationAddress _address;

    private PortAddress(ApplicationAddress address)
    {
        if (address.Kind is not (ApplicationAddressKind.WorkflowPort or ApplicationAddressKind.SystemPort))
            throw new FormatException("A port address must identify a workflow or system port.");
        _address = address;
    }

    public string Value => _address.Value;
    public string PortName => _address.PortName!;
    public ComponentAddress? ComponentAddress => _address.ComponentAddress is { } component
        ? new ComponentAddress(component) : null;
    public bool IsSystem => _address.Kind == ApplicationAddressKind.SystemPort;

    public static PortAddress Parse(string value) => new(ApplicationAddress.Parse(value));
    public static PortAddress Resolve(string reference, ComponentAddress component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return new(ApplicationAddress.ResolvePort(reference, component.WorkflowName));
    }

    internal static PortAddress Create(ComponentAddress component, string name)
        => new(ApplicationAddress.WorkflowPort(component.WorkflowName, component.ComponentName, name));

    public override string ToString() => Value;
}
