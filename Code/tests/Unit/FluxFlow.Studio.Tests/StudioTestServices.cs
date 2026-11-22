using FluxFlow.Studio.UI.Features.Plugins;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Authoring.Definitions;
using Microsoft.Extensions.DependencyInjection;

namespace FluxFlow.Studio.Tests;

internal static class StudioTestServices
{
    internal static FluxFlow.Plugins.PluginDescription[] BrokerCatalog =>
        [new FluxFlow.Plugins.Mqtt.MqttPlugin().Description, new FluxFlow.Plugins.RabbitMQ.RabbitMQPlugin().Description, new FluxFlow.Plugins.Nats.NatsPlugin().Description];

    internal static FluxFlow.Plugins.PluginDescription[] SourcesCatalog => [new FluxFlow.Plugins.Sources.SourcesPlugin().Description];
    internal static FluxFlow.Plugins.PluginDescription[] ProcessingCatalog => [.. SourcesCatalog, new FluxFlow.Plugins.Transformation.TransformationPlugin().Description, new FluxFlow.Plugins.FlowControl.FlowControlPlugin().Description];
    internal static FluxFlow.Plugins.PluginDescription[] InstalledCatalog => [new FluxFlow.Plugins.Bluetooth.BluetoothPlugin().Description, new FluxFlow.Plugins.Ros2.RosPlugin().Description, new FluxFlow.Plugins.Http.HttpPlugin().Description, .. BrokerCatalog, .. ProcessingCatalog, new FluxFlow.Plugins.Logging.LoggingPlugin().Description, new FluxFlow.Plugins.Routing.RoutingPlugin().Description];

    internal static FluxFlow.Plugins.PluginConnection BrokerConnection(string type) =>
        BrokerCatalog.SelectMany(plugin => plugin.Connections).Single(connection => connection.Type == type);

    public static void AddMqttDesign(IServiceCollection services) =>
        PortableProperties.Configure(services, new FluxFlow.Plugins.Mqtt.MqttPlugin().Description, new FluxFlow.Plugins.Mqtt.MqttDesign(), inProcess: false);

    public static FluxFlow.Studio.UI.Features.Connections.ConnectionDescription MqttConnection()
    {
        var services = new ServiceCollection();
        AddMqttDesign(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<FluxFlow.Studio.UI.Features.Connections.ConnectionDescription>();
    }

    public static void AddRabbitDesign(IServiceCollection services) =>
        PortableProperties.Configure(services, new FluxFlow.Plugins.RabbitMQ.RabbitMQPlugin().Description, new FluxFlow.Plugins.RabbitMQ.RabbitDesign(), inProcess: false);

    public static FluxFlow.Studio.UI.Features.Connections.ConnectionDescription RabbitConnection()
    {
        var services = new ServiceCollection();
        AddRabbitDesign(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<FluxFlow.Studio.UI.Features.Connections.ConnectionDescription>();
    }

    public static void AddNatsDesign(IServiceCollection services) =>
        PortableProperties.Configure(services, new FluxFlow.Plugins.Nats.NatsPlugin().Description, new FluxFlow.Plugins.Nats.NatsDesign(), inProcess: false);

    public static FluxFlow.Studio.UI.Features.Connections.ConnectionDescription NatsConnection()
    {
        var services = new ServiceCollection();
        AddNatsDesign(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<FluxFlow.Studio.UI.Features.Connections.ConnectionDescription>();
    }

    public static void AddSourcesPlugin(IServiceCollection services)
    {
        var plugin = new FluxFlow.Plugins.Sources.SourcesPlugin();
        foreach (var component in plugin.Description.Components)
            services.AddSingleton<IComponentDescription>(component);
        PortableProperties.Configure(services, plugin.Description, new FluxFlow.Plugins.Sources.SourcesDesign(), inProcess: false);
    }

    public static void AddLoggingPlugin(IServiceCollection services)
    {
        var plugin = new FluxFlow.Plugins.Logging.LoggingPlugin();
        foreach (var component in plugin.Description.Components)
            services.AddSingleton<IComponentDescription>(component);
        PortableProperties.Configure(services, plugin.Description, new FluxFlow.Plugins.Logging.LoggingDesign(), inProcess: false);
    }

    public static void AddRoutingPlugin(IServiceCollection services)
    {
        var plugin = new FluxFlow.Plugins.Routing.RoutingPlugin();
        foreach (var component in plugin.Description.Components)
            services.AddSingleton<IComponentDescription>(component);
        PortableProperties.Configure(services, plugin.Description, new FluxFlow.Plugins.Routing.RoutingDesign(), inProcess: false);
    }

    public static void AddTransformationPlugin(IServiceCollection services)
    {
        var plugin = new FluxFlow.Plugins.Transformation.TransformationPlugin();
        foreach (var component in plugin.Description.Components)
            services.AddSingleton<IComponentDescription>(component);
        PortableProperties.Configure(services, plugin.Description, new FluxFlow.Plugins.Transformation.TransformationDesign(), inProcess: false);
    }

    public static void AddFlowControlPlugin(IServiceCollection services)
    {
        var plugin = new FluxFlow.Plugins.FlowControl.FlowControlPlugin();
        foreach (var component in plugin.Description.Components)
            services.AddSingleton<IComponentDescription>(component);
        PortableProperties.Configure(services, plugin.Description, new FluxFlow.Plugins.FlowControl.FlowControlDesign(), inProcess: false);
    }

    public static void AddRosPlugin(IServiceCollection services)
    {
        var plugin = new FluxFlow.Plugins.Ros2.RosPlugin();
        foreach (var component in plugin.Description.Components)
            services.AddSingleton<IComponentDescription>(component);
        PortableProperties.Configure(services, plugin.Description, new FluxFlow.Plugins.Ros2.RosDesign(), inProcess: false);
    }

    public static void AddHttpPlugin(IServiceCollection services)
    {
        var plugin = new FluxFlow.Plugins.Http.HttpPlugin();
        foreach (var component in plugin.Description.Components)
            services.AddSingleton<IComponentDescription>(component);
        PortableProperties.Configure(services, plugin.Description, new FluxFlow.Plugins.Http.HttpDesign(), inProcess: false);
    }

    public static FluxFlow.Studio.UI.Features.Connections.ConnectionDescription HttpConnection()
    {
        var services = new ServiceCollection();
        AddHttpPlugin(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<FluxFlow.Studio.UI.Features.Connections.ConnectionDescription>();
    }

    public static void AddSerialPlugin(IServiceCollection services)
    {
        var plugin = new FluxFlow.Plugins.Serial.SerialPlugin();
        foreach (var component in plugin.Description.Components)
            services.AddSingleton<IComponentDescription>(component);
        PortableProperties.Configure(services, plugin.Description, new FluxFlow.Plugins.Serial.SerialDesign(), inProcess: false);
    }

    public static FluxFlow.Studio.UI.Features.Connections.ConnectionDescription SerialConnection()
    {
        var services = new ServiceCollection();
        AddSerialPlugin(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<FluxFlow.Studio.UI.Features.Connections.ConnectionDescription>();
    }

    public static DesignerGraphState CreateGraph()
    {
        var services = new ServiceCollection().AddCatalogComponents(BrokerCatalog);
        AddSourcesPlugin(services);
        AddLoggingPlugin(services);
        AddRoutingPlugin(services);
        AddTransformationPlugin(services);
        AddFlowControlPlugin(services);
        AddSerialPlugin(services);
        AddHttpPlugin(services);
        AddRosPlugin(services);
        var bluetooth = new FluxFlow.Plugins.Bluetooth.BluetoothPlugin();
        services.AddCatalogComponents([bluetooth.Description]);
        PortableProperties.Configure(services, bluetooth.Description, new FluxFlow.Plugins.Bluetooth.Design.BluetoothDesign(), inProcess: false);
        AddRabbitDesign(services);
        AddNatsDesign(services);
        AddMqttDesign(services);
        using var provider = services.BuildServiceProvider();
        return Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<DesignerGraphState>(provider);
    }
}
