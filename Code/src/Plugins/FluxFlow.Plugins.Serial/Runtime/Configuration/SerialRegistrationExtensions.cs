using System.IO.Ports;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Components.Serial;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Configuration.Components.Serial;

public static class SerialRegistrationExtensions
{
    /// <summary>Registers workflow-local serial resources and components without opening ports.</summary>
    public static IServiceCollection AddSerial(this IServiceCollection services)
    {
        services.AddKeyedSingleton<ResourceRegistration>("serial.port", (registrations, address, section) =>
        {
            var config = section.Get<SerialPortConfiguration>() ?? throw new ArgumentException("Missing serial port settings.");
            registrations.AddSerialPort(address.Value, config);
        });

        services.AddKeyedSingleton<ComponentRegistration>("serial.text", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<SerialTextConfiguration>();
            RegisterText(registrations, address, config);
            registrations.AddLinks(address, config);
        });

        services.AddKeyedSingleton<ComponentRegistration>("serial.reader", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<SerialFrameConfiguration>();
            RegisterReader(registrations, address, config);
            registrations.AddLinks(address, config);
        });
        services.AddKeyedSingleton<ComponentRegistration>("serial.writer", (registrations, section) =>
        {
            var address = section.GetComponentAddress();
            var config = section.ReadRequiredConfiguration<SerialFrameConfiguration>();
            RegisterWriter(registrations, address, config);
            registrations.AddLinks(address, config);
        });
        return services;
    }

    public static IServiceCollection AddSerialPort(this IServiceCollection registrations, string path, SerialPortConfiguration config)
    {
        var address = ApplicationAddress.Parse(path);
        if (address.WorkflowName is null)
            throw new ArgumentException("A serial.port resource must belong to a workflow.");
        SerialPortLimits.Validate(config.PortName, config.BaudRate, config.DataBits, config.ReadTimeout, config.WriteTimeout,
            config.RtsEnable, config.Handshake is Handshake.RequestToSend or Handshake.RequestToSendXOnXOff);
        if (!Enum.IsDefined(config.Parity)) throw new ArgumentOutOfRangeException(nameof(config.Parity));
        if (!Enum.IsDefined(config.StopBits) || config.StopBits == StopBits.None) throw new ArgumentOutOfRangeException(nameof(config.StopBits));
        if (!Enum.IsDefined(config.Handshake)) throw new ArgumentOutOfRangeException(nameof(config.Handshake));
        var physicalPort = OperatingSystem.IsWindows() ? config.PortName.Trim().Replace(@"\\.\", "").ToUpperInvariant() : config.PortName.Trim();
        if (registrations.Any(item => item.IsKeyedService && item.ServiceType == typeof(PortOwner)
            && item.KeyedImplementationInstance is PortOwner owner && owner.Name == physicalPort))
            throw new ArgumentException($"Serial port '{config.PortName}' is already declared. Share one resource between its Reader and Writers.");
        registrations.AddKeyedSingleton(address.Value, new PortOwner(physicalPort));
        registrations.AddFluxFlowResource(address, _ => new SerialPort(config.PortName, config.BaudRate,
            config.Parity, config.DataBits, config.StopBits)
        {
            Handshake = config.Handshake, DtrEnable = config.DtrEnable, RtsEnable = config.RtsEnable,
            ReadTimeout = (int)config.ReadTimeout.TotalMilliseconds, WriteTimeout = (int)config.WriteTimeout.TotalMilliseconds
        });
        registrations.AddFluxFlowResource(address, provider => new SerialSession(provider.GetRequiredKeyedService<SerialPort>(address.Value)));
        return registrations;
    }

    public static WorkflowBuilder SerialText(this WorkflowBuilder workflow, string name, SerialTextConfiguration config)
    {
        RegisterText(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterText(IServiceCollection registrations, ComponentAddress address, SerialTextConfiguration config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Connection);
        var connection = ApplicationAddress.ResolveResourcePaths(config.Connection, address.WorkflowName)[0];
        if (connection.WorkflowName != address.WorkflowName)
            throw new ArgumentException("SerialText requires a resource in its own workflow.");
        if (!registrations.Any(item => item.IsKeyedService && item.ServiceType == typeof(SerialPort)
            && Equals(item.ServiceKey, connection.Value) && item.Lifetime == ServiceLifetime.Scoped))
            throw new ArgumentException($"Workflow-local serial port '{connection}' is not registered.");

        var options = new SerialTextOptions
        {
            NewLine = config.NewLine, MaxLineBytes = config.MaxLineBytes,
            InputCapacity = config.InputCapacity, OutputCapacity = config.OutputCapacity
        };
        options.Validate();
        if (Has<ReaderOwner>(registrations, connection) || Has<WriterOwner>(registrations, connection))
            throw new ArgumentException($"Serial port '{connection}' already has a Reader or Writer. Serial Text requires exclusive ownership.");
        // One options registration identifies the one text session using this port.
        if (registrations.Any(item => item.IsKeyedService && item.ServiceType == typeof(SerialTextOptions)
            && Equals(item.ServiceKey, connection.Value)))
            throw new ArgumentException($"Serial port '{connection}' already has a SerialText owner. Use output links for fan-out.");
        registrations.AddKeyedSingleton(connection.Value, options);
        registrations.AddComponentFactory(address, provider => new SerialText(
            provider.GetRequiredKeyedService<SerialPort>(connection.Value),
            provider.GetRequiredKeyedService<SerialTextOptions>(connection.Value)))
            .AddInputPort<SerialText>(address, "Input", node => node.Input)
            .AddOutputPort<SerialText>(address, "Output", node => node.Output)
            .AddEventPort<SerialText>(address, "Events", node => node.Events);
    }

    public static WorkflowBuilder SerialReader(this WorkflowBuilder workflow, string name, SerialFrameConfiguration config)
    {
        RegisterReader(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterReader(IServiceCollection registrations, ComponentAddress address, SerialFrameConfiguration config)
    {
        var connection = SharedConnection(registrations, address, config);
        if (Has<ReaderOwner>(registrations, connection))
            throw new ArgumentException($"Serial port '{connection}' already has a Reader. Use links for fan-out.");
        registrations.AddKeyedSingleton(connection.Value, new ReaderOwner());
        registrations.AddComponentFactory(address, provider => new SerialReader(
                provider.GetRequiredKeyedService<SerialSession>(connection.Value), config.Options()))
            .AddOutputPort<SerialReader>(address, "Output", node => node.Output)
            .AddEventPort<SerialReader>(address, "Events", node => node.Events);
    }

    public static WorkflowBuilder SerialWriter(this WorkflowBuilder workflow, string name, SerialFrameConfiguration config)
    {
        RegisterWriter(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterWriter(IServiceCollection registrations, ComponentAddress address, SerialFrameConfiguration config)
    {
        var connection = SharedConnection(registrations, address, config);
        if (!Has<WriterOwner>(registrations, connection)) registrations.AddKeyedSingleton(connection.Value, new WriterOwner());
        registrations.AddComponentFactory(address, provider => new SerialWriter(
                provider.GetRequiredKeyedService<SerialSession>(connection.Value), config.Options()))
            .AddInputPort<SerialWriter>(address, "Input", node => node.Input)
            .AddOutputPort<SerialWriter>(address, "Output", node => node.Output)
            .AddEventPort<SerialWriter>(address, "Events", node => node.Events);
    }

    private sealed record PortOwner(string Name);
    private sealed record ReaderOwner;
    private sealed record WriterOwner;
    private static bool Has<T>(IServiceCollection registrations, ApplicationAddress connection) =>
        registrations.Any(item => item.IsKeyedService && item.ServiceType == typeof(T) && Equals(item.ServiceKey, connection.Value));
    private static ApplicationAddress SharedConnection(IServiceCollection registrations, ComponentAddress address, SerialFrameConfiguration config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Connection);
        config.Options().Validate();
        var connection = ApplicationAddress.ResolveResourcePaths(config.Connection, address.WorkflowName)[0];
        if (connection.WorkflowName != address.WorkflowName || !registrations.Any(item => item.IsKeyedService &&
            item.ServiceType == typeof(SerialSession) && Equals(item.ServiceKey, connection.Value)))
            throw new ArgumentException("Serial Reader and Writer require a serial.port resource in their own workflow.");
        if (Has<SerialTextOptions>(registrations, connection))
            throw new ArgumentException($"Serial port '{connection}' is owned by Serial Text. Use Reader and Writer together instead.");
        return connection;
    }
}
