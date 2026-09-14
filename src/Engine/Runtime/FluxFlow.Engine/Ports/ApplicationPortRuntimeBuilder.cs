using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Core;
using FluxFlow.Engine.Signals;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Engine.Ports;

internal sealed class ApplicationPortRuntimeBuilder
{
    public const int DefaultInputCapacity = 128;
    public const int DefaultOutputCapacity = 256;

    public const int DefaultSystemOutputCapacity = ApplicationRuntimeSignals.Capacity;

    private static readonly IReadOnlyList<ApplicationSystemOutputMetadata> SystemOutputValues =
        Array.AsReadOnly<ApplicationSystemOutputMetadata>(
        [
            ApplicationSystemOutputMetadata.Create<global::FluxFlow.Data.FlowValue>(
                ApplicationAddress.SystemEvents),
            ApplicationSystemOutputMetadata.Create<global::FluxFlow.Data.FlowValue>(
                ApplicationAddress.SystemDiagnostics)
        ]);

    private readonly Dictionary<ApplicationAddress, PortRegistration> _ports = [];
    private readonly IApplicationOutputCaptureResolver? _outputCaptures;
    private readonly int _requestCapacity;
    private readonly TimeProvider? _timeProvider;
    private ILogger? _logger;

    public ApplicationPortRuntimeBuilder(
        IApplicationOutputCaptureResolver? outputCaptures = null,
        int requestCapacity = 1024,
        TimeProvider? timeProvider = null)
    {
        _outputCaptures = outputCaptures;
        _requestCapacity = requestCapacity;
        _timeProvider = timeProvider;
        Add(CreateOutputRegistration(
            ApplicationAddress.SystemEvents,
            DefaultSystemOutputCapacity));
        Add(CreateOutputRegistration(
            ApplicationAddress.SystemDiagnostics,
            DefaultSystemOutputCapacity));
    }

    public static IReadOnlyList<ApplicationSystemOutputMetadata> SystemOutputs => SystemOutputValues;

    public ApplicationPortRuntimeBuilder UseLogger(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        return this;
    }

    public ApplicationPortRuntimeBuilder AddInput(
        ApplicationAddress address,
        int capacity = DefaultInputCapacity)
    {
        ValidateAddress(address, ApplicationPortDirection.Input);
        Add(new PortRegistration(
            address,
            ApplicationPortDirection.Input,
            ApplicationPortKind.Message,
            typeof(global::FluxFlow.Data.FlowValue),
            capacity,
            (report, activity) => new ApplicationInputPort(address, capacity, report, activity),
            null));
        return this;
    }

    public ApplicationPortRuntimeBuilder AddSignalInput(
        ApplicationAddress address,
        int capacity = DefaultInputCapacity)
    {
        ValidateAddress(address, ApplicationPortDirection.Input);
        Add(new PortRegistration(
            address,
            ApplicationPortDirection.Input,
            ApplicationPortKind.Signal,
            typeof(object),
            capacity,
            (report, activity) => new ApplicationInputPort(address, capacity, report, activity, ApplicationPortKind.Signal),
            null));
        return this;
    }

    public ApplicationPortRuntimeBuilder AddOutput(
        ApplicationAddress address,
        int capacity = DefaultOutputCapacity,
        bool discardUnmatched = false)
    {
        ValidateAddress(address, ApplicationPortDirection.Output);
        Add(CreateOutputRegistration(address, capacity, discardUnmatched));
        return this;
    }

    public ApplicationPortRuntime Build()
        => new(_ports.Values
            .OrderBy(static registration => registration.Address.Value, StringComparer.Ordinal)
            .ToArray(),
            _logger,
            _requestCapacity,
            _timeProvider);

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

    private PortRegistration CreateOutputRegistration(
        ApplicationAddress address,
        int capacity,
        bool discardUnmatched = false)
    {
        var capture = _outputCaptures?.Resolve(address);
        return new PortRegistration(
            address,
            ApplicationPortDirection.Output,
            ApplicationPortKind.Message,
            typeof(global::FluxFlow.Data.FlowValue),
            capacity,
            null,
            (report, activity, revisionRouting, exchanges) => new ApplicationOutputPort(
                address,
                capacity,
                report,
                activity,
                revisionRouting,
                capture,
                exchanges, discardUnmatched || address.Kind == ApplicationAddressKind.SystemPort));
    }

    internal sealed record PortRegistration(
        ApplicationAddress Address,
        ApplicationPortDirection Direction,
        ApplicationPortKind Kind,
        Type PayloadType,
        int Capacity,
        Func<Action<ApplicationPortRejection>, Action<ApplicationPortActivity>, IApplicationInputPort>? CreateInput,
        Func<Action<ApplicationPortRejection>, Action<ApplicationPortActivity>, ApplicationRevisionRouting, FlowRequestReply, IApplicationOutputPort>? CreateOutput);
}
