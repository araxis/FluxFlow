using FluxFlow.Composition.Addressing;

namespace FluxFlow.Engine.Ports;

public sealed class ApplicationPortRuntimeBuilder
{
    public const int DefaultInputCapacity = 128;
    public const int DefaultOutputCapacity = 256;

    private readonly Dictionary<ApplicationAddress, PortRegistration> _ports = [];

    public ApplicationPortRuntimeBuilder AddInput<T>(
        ApplicationAddress address,
        int capacity = DefaultInputCapacity)
    {
        ValidateAddress(address, ApplicationPortDirection.Input);
        Add(new PortRegistration(
            address,
            ApplicationPortDirection.Input,
            typeof(T),
            capacity,
            report => new ApplicationInputPort<T>(address, capacity, report),
            null));
        return this;
    }

    public ApplicationPortRuntimeBuilder AddOutput<T>(
        ApplicationAddress address,
        int capacity = DefaultOutputCapacity)
    {
        ValidateAddress(address, ApplicationPortDirection.Output);
        Add(new PortRegistration(
            address,
            ApplicationPortDirection.Output,
            typeof(T),
            capacity,
            null,
            report => new ApplicationOutputPort<T>(address, capacity, report)));
        return this;
    }

    public ApplicationPortRuntime Build()
        => new(_ports.Values
            .OrderBy(static registration => registration.Address.Value, StringComparer.Ordinal)
            .ToArray());

    private void Add(PortRegistration registration)
    {
        if (registration.Capacity <= 0)
            throw new ArgumentOutOfRangeException("capacity", "Port capacity must be greater than zero.");
        if (!_ports.TryAdd(registration.Address, registration))
            throw new InvalidOperationException($"Application port '{registration.Address}' is already registered.");
    }

    private static void ValidateAddress(
        ApplicationAddress address,
        ApplicationPortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.Kind == ApplicationAddressKind.Resource)
            throw new ArgumentException("Resource addresses cannot be registered as runtime ports.", nameof(address));
        if (direction == ApplicationPortDirection.Input &&
            address.Kind == ApplicationAddressKind.SystemPort)
        {
            throw new ArgumentException("Reserved system ports are output-only.", nameof(address));
        }
    }

    internal sealed record PortRegistration(
        ApplicationAddress Address,
        ApplicationPortDirection Direction,
        Type PayloadType,
        int Capacity,
        Func<Action<ApplicationPortRejection>, IApplicationInputPort>? CreateInput,
        Func<Action<ApplicationPortRejection>, IApplicationOutputPort>? CreateOutput);
}
