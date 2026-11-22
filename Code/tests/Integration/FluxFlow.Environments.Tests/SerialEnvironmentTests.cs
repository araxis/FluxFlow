using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluxFlow.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class SerialEnvironmentTests
{
    private const string Config = """{"Workflows":{"device":{"Resources":{"port":{"Type":"serial.port","PortName":"original","BaudRate":9600}},"read":{"Type":"serial.reader","Connection":"port"},"write":{"Type":"serial.writer","Connection":"port"}}}}""";
    private const string Path = "Workflows:device:Resources:port";
    private static ApplicationInstance Instance(string id, string port) => new(id, id)
    { ExistingServices = new() { [Path] = new() { Type = "serial.port", Endpoint = "serial-port://local/" + Uri.EscapeDataString(port) } } };

    [Fact]
    public async Task Physical_bindings_change_only_port_names_and_remain_container_free()
    {
        var preparation = new EnvironmentPreparation([new SerialEnvironmentAdapter()], []);
        var request = new EnvironmentRunRequest(Config, new()) { Instances = [Instance("a", "COM3"), Instance("b", "/dev/ttyUSB0")] };
        preparation.ValidateBindings(request);
        await using var prepared = await preparation.PrepareAsync(Config, request.Environment);
        var first = JsonNode.Parse(preparation.BindConfiguration(prepared, request.Instances[1]))!["Workflows"]!["device"]!["Resources"]!["port"]!;
        first["PortName"]!.GetValue<string>().ShouldBe("/dev/ttyUSB0"); first["BaudRate"]!.GetValue<int>().ShouldBe(9600);
        prepared.Services.ShouldAllBe(service => service.ContainerId == null && service.Provider == "existing");
        JsonNode.DeepEquals(prepared.Configuration, JsonNode.Parse(Config)).ShouldBeTrue();
    }

    [Fact]
    public void Multi_instance_physical_ownership_is_rejected_before_start()
    {
        var preparation = new EnvironmentPreparation([new SerialEnvironmentAdapter()], []);
        var request = new EnvironmentRunRequest(Config, new()) { Instances = [Instance("a", "COM3"), Instance("b", "COM3")] };
        Should.Throw<ArgumentException>(() => preparation.ValidateBindings(request)).Message.ShouldContain("already has an owner");
    }

    [Fact]
    public async Task Check_and_on_demand_discovery_are_read_only_and_discovery_requires_local_guard()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        var discovery = new Discovery(); builder.Services.AddSingleton<ISerialPortDiscovery>(discovery);
        using var plugins = new FluxFlow.Execution.Plugins.InstalledPlugins(new FluxFlow.Plugins.Serial.SerialPlugin());
        builder.Services.AddLocalFlowRunner(plugins);
        await using var app = builder.Build(); app.MapLocalFlowRunner(); await app.StartAsync();
        using var client = app.GetTestClient();
        var runner = new FluxFlow.DesignerApp.Services.HttpEnvironmentRunner(client);
        var check = await runner.CheckAsync(new(Config, new()), CancellationToken.None);
        check.Success.ShouldBeTrue(check.Message); check.Message.ShouldContain("Docker is not required");
        discovery.Calls.ShouldBe(0);
        using (var denied = await client.PostAsync("/api/runs/serial/ports", null)) denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        discovery.Calls.ShouldBe(0);
        client.DefaultRequestHeaders.Add("X-FluxFlow-Run", "1");
        using var response = await client.PostAsync("/api/runs/serial/ports", null);
        response.EnsureSuccessStatusCode();
        (await response.Content.ReadFromJsonAsync(SerialDiscoveryJson.Default.SerialPortList))!.Ports.ShouldBe(new[] { "COM3" });
        discovery.Calls.ShouldBe(1);
        var plugin = plugins.Descriptions.Single();
        var port = plugin.Connections.Single().Sections.SelectMany(s => s.Options).Single(f => f.Suggestions != null);
        var path = $"/api/runs/plugins/{plugin.Id}/suggestions/{port.Suggestions!.Operation}";
        using var listed = await client.PostAsync(path, null);
        listed.EnsureSuccessStatusCode();
        (await listed.Content.ReadFromJsonAsync<string[]>()).ShouldBe(new[] { "COM3" });
        using var missing = await client.PostAsync(path + "-missing", null);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        client.DefaultRequestHeaders.Remove("X-FluxFlow-Run");
        using var guarded = await client.PostAsync(path, null);
        guarded.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        discovery.Calls.ShouldBe(2);
    }
    private sealed class Discovery : ISerialPortDiscovery
    {
        public int Calls;
        public Task<string[]> ListAsync(CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(new[] { "COM3" }); }
    }
}
