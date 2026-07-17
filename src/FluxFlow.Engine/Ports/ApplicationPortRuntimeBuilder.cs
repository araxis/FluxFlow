using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Engine.Signals;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Engine.Ports;

public sealed class ApplicationPortRuntimeBuilder
{
    public const int DefaultInputCapacity = 128;
    public const int DefaultOutputCapacity = 256;

    public const int DefaultSystemOutputCapacity = ApplicationRuntimeSignals.Capacity;

    private static readonly IReadOnlyList<ApplicationSystemOutputMetadata> SystemOutputValues =
        Array.AsReadOnly<ApplicationSystemOutputMetadata>(
        [
            ApplicationSystemOutputMetadata.Create<ApplicationSystemEvent>(
                ApplicationAddress.SystemEvents),
            ApplicationSystemOutputMetadata.Create<ApplicationDiagnostic>(
                ApplicationAddress.SystemDiagnostics)
        ]);

    private readonly Dictionary<ApplicationAddress, PortRegistration> _ports = [];
    private ILogger? _logger;

    public ApplicationPortRuntimeBuilder()
    {
        Add(CreateOutputRegistration<ApplicationSystemEvent>(
            ApplicationAddress.SystemEvents,
            DefaultSystemOutputCapacity));
        Add(CreateOutputRegistration<ApplicationDiagnostic>(
            ApplicationAddress.SystemDiagnostics,
            DefaultSystemOutputCapacity));
    }

    public static IReadOnlyList<ApplicationSystemOutputMetadata> SystemOutputs => SystemOutputValues;

    public ApplicationPortRuntimeBuilder UseLogger(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        return this;
    }

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
            (report, activity) => new ApplicationInputPort<T>(address, capacity, report, activity),
            null));
        return this;
    }

    public ApplicationPortRuntimeBuilder AddOutput<T>(
        ApplicationAddress address,
        int capacity = DefaultOutputCapacity)
    {
        ValidateAddress(address, ApplicationPortDirection.Output);
        Add(CreateOutputRegistration<T>(address, capacity));
        return this;
    }

    public ApplicationPortRuntime Build()
        => new(_ports.Values
            .OrderBy(static registration => registration.Address.Value, StringComparer.Ordinal)
            .ToArray(),
            _logger);

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
        if (direction == ApplicationPortDirection.Output &&
            address.Kind == ApplicationAddressKind.SystemPort)
        {
            throw new ArgumentException(
                "Reserved system outputs are registered automatically by the runtime builder.",
                nameof(address));
        }
    }

    private static PortRegistration CreateOutputRegistration<T>(
        ApplicationAddress address,
        int capacity)
        => new(
            address,
            ApplicationPortDirection.Output,
            typeof(T),
            capacity,
            null,
            (report, activity) => new ApplicationOutputPort<T>(
                address,
                capacity,
                report,
                activity));

    internal sealed record PortRegistration(
        ApplicationAddress Address,
        ApplicationPortDirection Direction,
        Type PayloadType,
        int Capacity,
        Func<Action<ApplicationPortRejection>, Action<ApplicationPortActivity>, IApplicationInputPort>? CreateInput,
        Func<Action<ApplicationPortRejection>, Action<ApplicationPortActivity>, IApplicationOutputPort>? CreateOutput);
}
