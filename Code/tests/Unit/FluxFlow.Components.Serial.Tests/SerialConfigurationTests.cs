using System.IO.Ports;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Serial;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Serial.Tests;

public sealed class SerialConfigurationTests
{
    [Fact]
    public async Task Reader_and_multiple_writers_resolve_one_workflow_owned_session_without_opening()
    {
        var settings = Settings(); settings["Workflows:device:serial:Type"] = "serial.reader";
        settings["Workflows:device:write:Type"] = "serial.writer"; settings["Workflows:device:write:Connection"] = "port";
        settings["Workflows:device:otherWrite:Type"] = "serial.writer"; settings["Workflows:device:otherWrite:Connection"] = "device.Resources.port";
        var services = new ServiceCollection().AddSerial(); services.AddFluxFlowApplication(Config(settings));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var app = await provider.BuildApplicationAsync();
        var workflow = provider.GetRequiredKeyedService<FlowWorkflow>("device");
        workflow.Services.GetRequiredKeyedService<IFlowNode>("device.serial").ShouldBeOfType<SerialReader>();
        workflow.Services.GetRequiredKeyedService<IFlowNode>("device.write").ShouldBeOfType<SerialWriter>();
        workflow.Services.GetRequiredKeyedService<SerialSession>("device.Resources.port")
            .ShouldBeSameAs(workflow.Services.GetRequiredKeyedService<SerialSession>("device.Resources.port"));
        workflow.Services.GetRequiredKeyedService<SerialPort>("device.Resources.port").IsOpen.ShouldBeFalse();
        await app.StopAsync();
    }

    [Theory]
    [InlineData("serial.reader", "serial.reader")]
    [InlineData("serial.reader", "serial.text")]
    [InlineData("serial.writer", "serial.text")]
    [InlineData("serial.text", "serial.writer")]
    public void Conflicting_owners_reject_before_hardware_access(string first, string second)
    {
        var settings = Settings(); settings["Workflows:device:serial:Type"] = first;
        settings["Workflows:device:other:Type"] = second; settings["Workflows:device:other:Connection"] = "port";
        Reject(settings);
    }

    [Fact]
    public void Aliased_resources_cannot_claim_same_physical_port_across_workflows()
    {
        var settings = Settings();
        settings["Workflows:other:Resources:alias:Type"] = "serial.port";
        settings["Workflows:other:Resources:alias:PortName"] = "UnavailableTestPort";
        settings["Workflows:other:read:Type"] = "serial.reader";
        settings["Workflows:other:read:Connection"] = "alias";
        Reject(settings);
    }

    [Fact]
    public async Task Configuration_registers_and_builds_without_opening_hardware()
    {
        var services = new ServiceCollection().AddSerial();
        services.AddFluxFlowApplication(Config(Settings()));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var app = await provider.BuildApplicationAsync();
        var workflow = provider.GetRequiredKeyedService<FlowWorkflow>("device");
        var port = workflow.Services.GetRequiredKeyedService<SerialPort>("device.Resources.port");
        port.IsOpen.ShouldBeFalse();
        port.PortName.ShouldBe("UnavailableTestPort");
        port.BaudRate.ShouldBe(115200);
        port.ReadTimeout.ShouldBe(250);
        port.WriteTimeout.ShouldBe(2000);
        workflow.Services.GetRequiredKeyedService<IFlowNode>("device.serial").ShouldBeOfType<SerialText>();
        Should.Throw<InvalidOperationException>(() => provider.GetRequiredKeyedService<SerialPort>("device.Resources.port"));
        await app.StopAsync();
    }

    [Theory]
    [InlineData("BaudRate", "0")]
    [InlineData("DataBits", "9")]
    [InlineData("Parity", "999")]
    [InlineData("StopBits", "None")]
    [InlineData("WriteTimeout", "00:00:00")]
    [InlineData("PortName", " ")]
    public void Bad_resource_settings_reject_transactionally(string property, string value)
    {
        var settings = Settings();
        settings["Workflows:device:Resources:port:" + property] = value;
        Reject(settings);
    }

    [Theory]
    [InlineData("Connection", "Resources.port")]
    [InlineData("Connection", "other.Resources.port")]
    [InlineData("Connection", "missing")]
    [InlineData("NewLine", "x")]
    [InlineData("MaxLineBytes", "0")]
    [InlineData("InputCapacity", "0")]
    [InlineData("OutputCapacity", "0")]
    public void Bad_component_settings_reject_transactionally(string property, string value)
    {
        var settings = Settings();
        settings["Workflows:device:serial:" + property] = value;
        Reject(settings);
    }

    [Fact]
    public void Application_level_serial_resource_is_rejected()
    {
        var settings = Settings();
        settings["Resources:shared:Type"] = "serial.port";
        settings["Resources:shared:PortName"] = "UnavailableTestPort";
        Reject(settings);
    }

    [Theory]
    [InlineData("port")]
    [InlineData("device.Resources.port")]
    public void Second_reader_is_rejected_even_with_a_different_reference_spelling(string reference)
    {
        var settings = Settings();
        settings["Workflows:device:second:Type"] = "serial.text";
        settings["Workflows:device:second:Connection"] = reference;
        Reject(settings);
    }

    private static void Reject(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection().AddSerial();
        var before = services.ToArray();
        Should.Throw<InvalidOperationException>(() => services.AddFluxFlowApplication(Config(settings)));
        services.ToArray().ShouldBe(before);
    }

    private static IConfiguration Config(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static Dictionary<string, string?> Settings() => new()
    {
        ["Workflows:device:Resources:port:Type"] = "serial.port",
        ["Workflows:device:Resources:port:PortName"] = "UnavailableTestPort",
        ["Workflows:device:serial:Type"] = "serial.text",
        ["Workflows:device:serial:Connection"] = "port"
    };
}
