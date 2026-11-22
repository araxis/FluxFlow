using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Components.Bluetooth;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Application.Links;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FluxFlow.Configuration.Components.Bluetooth;

public static class BleRegistrationExtensions
{
    public static IServiceCollection AddBluetooth(this IServiceCollection services)
    {
        services.TryAddSingleton<IBlePlatform, UnavailableBlePlatform>();
        services.AddKeyedSingleton<ResourceRegistration>("ble.peripheral", (registrations, address, section) =>
        {
            registrations.AddBlePeripheral(address.Value, section["Endpoint"] ?? "");
        });
        services.AddKeyedSingleton<ResourceRegistration>("ble.adapter", (registrations, address, section) =>
        {
            registrations.AddBleAdapter(address.Value, section["Endpoint"] ?? "");
        });
        services.AddKeyedSingleton<ComponentRegistration>("ble.client", (registrations, section) =>
        {
            var config = section.ReadRequiredConfiguration<BleClientConfiguration>();
            var address = section.GetComponentAddress();
            RegisterBleClient(registrations, address, config);
            registrations.AddLinks(address, config);
        });
        services.AddKeyedSingleton<ComponentRegistration>("ble.notifications", (registrations, section) =>
        {
            if (section.GetSection("Input").Exists()) throw new ArgumentException("BLE Notifications is a source and has no Input port.");
            var config = section.ReadRequiredConfiguration<BleClientConfiguration>();
            var address = section.GetComponentAddress();
            RegisterBleNotifications(registrations, address, config);
            registrations.AddLinks(address, config);
        });
        services.AddKeyedSingleton<ComponentRegistration>("ble.server", (registrations, section) =>
        {
            var config = section.ReadRequiredConfiguration<BleServerConfiguration>();
            var address = section.GetComponentAddress();
            RegisterBleGattServer(registrations, address, config);
            registrations.AddLinks(address, config);
        });
        return services;
    }

    public static IServiceCollection AddBlePeripheral(this IServiceCollection registrations, string path, string endpoint)
    {
        var resource = new BleConnection(endpoint);
        resource.Apply(new());
        return registrations.AddFluxFlowResource(ApplicationAddress.Parse(path), _ => resource);
    }

    public static IServiceCollection AddBleAdapter(this IServiceCollection registrations, string path, string endpoint)
    {
        var resource = new BleConnection(endpoint);
        resource.ValidateAdapter();
        return registrations.AddFluxFlowResource(ApplicationAddress.Parse(path), _ => resource);
    }

    public static WorkflowBuilder BleClient(this WorkflowBuilder workflow, string name, BleClientConfiguration config)
    {
        RegisterBleClient(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterBleClient(IServiceCollection registrations, ComponentAddress address, BleClientConfiguration config)
    {
        var options = config.ToOptions();
        config.Validate(false);
        registrations.AddComponentFactory(address, provider => new BleClient(provider.GetRequiredService<IBlePlatform>(),
            config.Connection is null ? options : provider.GetRequiredFlowResource<BleConnection>(config.Connection, address.WorkflowName).Apply(options)))
            .AddInputPort<BleClient>(address, "Input", node => node.Input)
            .AddOutputPort<BleClient>(address, "Output", node => node.Output)
            .AddEventPort<BleClient>(address, "Events", node => node.Events);
    }

    public static WorkflowBuilder BleNotifications(this WorkflowBuilder workflow, string name, BleClientConfiguration config)
    {
        RegisterBleNotifications(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterBleNotifications(IServiceCollection registrations, ComponentAddress address, BleClientConfiguration config)
    {
        var options = config.ToOptions();
        config.Validate(true);
        registrations.AddComponentFactory(address, provider => new BleNotifications(provider.GetRequiredService<IBlePlatform>(),
            config.Connection is null ? options : provider.GetRequiredFlowResource<BleConnection>(config.Connection, address.WorkflowName).Apply(options)))
            .AddOutputPort<BleNotifications>(address, "Output", node => node.Output)
            .AddEventPort<BleNotifications>(address, "Events", node => node.Events);
    }

    public static WorkflowBuilder BleGattServer(this WorkflowBuilder workflow, string name, BleServerConfiguration config)
    {
        RegisterBleGattServer(workflow.Services, workflow.Address(name), config);
        return workflow;
    }

    private static void RegisterBleGattServer(IServiceCollection registrations, ComponentAddress address, BleServerConfiguration config)
    {
        var options = config.ToOptions();
        options.Validate();
        if (config.Adapter is not null) ArgumentException.ThrowIfNullOrWhiteSpace(config.Adapter);
        registrations.AddComponentFactory(address, provider =>
            {
                if (config.Adapter is not null) provider.GetRequiredFlowResource<BleConnection>(config.Adapter, address.WorkflowName).ValidateAdapter();
                return new BleGattServer(provider.GetRequiredService<IBlePlatform>(), options);
            })
            .AddInputPort<BleGattServer>(address, "Input", node => node.Input)
            .AddOutputPort<BleGattServer>(address, "Output", node => node.Output)
            .AddEventPort<BleGattServer>(address, "Events", node => node.Events);
    }
}
