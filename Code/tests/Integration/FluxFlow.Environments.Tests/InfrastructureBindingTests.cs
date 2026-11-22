using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using FluxFlow.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class InfrastructureBindingTests
{
    [Fact]
    public async Task Companion_routes_instances_to_existing_http_services_and_stop_leaves_server_running()
    {
        var serverBuilder = WebApplication.CreateBuilder();
        serverBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = serverBuilder.Build();
        var requests = new ConcurrentBag<string>();
        server.MapPost("/{instance}/heartbeat", (string instance) => { requests.Add(instance); return "ok"; });
        server.MapGet("/health", () => "ready");
        await server.StartAsync();
        var endpoint = server.Urls.Single();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        using var plugins = RunTestPlugins.WithBasicComponents(new FluxFlow.Plugins.Http.HttpPlugin());
        builder.Services.AddLocalFlowRunner(plugins);
        await using var companion = builder.Build();
        companion.MapLocalFlowRunner();
        await companion.StartAsync();
        using var client = companion.GetTestClient();
        var runner = new FluxFlow.DesignerApp.Services.HttpEnvironmentRunner(client);
        var request = new EnvironmentRunRequest("""
            {"Resources":{"api":{"Type":"http.client","BaseAddress":"http://unused.invalid/","Timeout":"00:00:02"}},
             "Workflows":{"main":{
               "tick":{"Type":"scheduler","Interval":"00:00:00.050","Output":"format.Input"},
               "format":{"Type":"mapper","Engine":"jsonata","Expression":"{'Method':'POST','RequestUrl':'heartbeat'}","Output":"send.Input"},
               "send":{"Type":"http.client","Client":"api"}
             }}}
            """, new() { Resources = new() { ["Resources:api"] = new() { Provider = "wiremock", Image = "unused:latest" } } })
        {
            Instances = new[] { "first", "second" }.Select(id => new ApplicationInstance(id, id)
            {
                ExistingServices = new() { ["Resources:api"] = new() { Type = "http.client", Endpoint = endpoint + "/" + id + "/" } }
            }).ToArray()
        };
        var original = JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var runId = Guid.NewGuid();
        try
        {
            (await runner.CheckAsync(request, deadline.Token)).Success.ShouldBeTrue();
            var status = await runner.StartAsync(runId, request, deadline.Token);
            status.State.ShouldBe("Running", status.Error);
            while (requests.Count(item => item == "first") < 2 || requests.Count(item => item == "second") < 2)
                await Task.Delay(25, deadline.Token);
            status.Services.ShouldAllBe(service => service.Provider == "existing" && service.ContainerId == null);
            await runner.StopAsync(runId, deadline.Token);
            (await runner.GetAsync(runId, deadline.Token)).State.ShouldBe("Stopped");
            using var probe = new HttpClient();
            (await probe.GetStringAsync(endpoint + "/health", deadline.Token)).ShouldBe("ready");
            JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest).ShouldBe(original);
        }
        finally
        {
            await runner.StopAsync(runId, CancellationToken.None);
            await companion.StopAsync();
            await server.StopAsync();
        }
    }

    private const string Configuration = """{"Resources":{"broker":{"Type":"mqtt.client","Host":"old","Port":1883,"Username":"user","Password":"secret","ClientId":"Keep"},"api":{"Type":"http.client","BaseAddress":"https://old/v1/","Headers":{"Authorization":"Bearer token"}}},"Workflows":{"main":{}}}""";
    private static EnvironmentPreparation Create() => new([new MqttEnvironmentAdapter(), new HttpEnvironmentAdapter()], []);
    private static ApplicationInstance Instance(string id) => new(id, id) { ExistingServices = new()
    {
        ["resources:BROKER"] = new() { Endpoint = "mqtts://p1:8883" },
        ["Resources:api"] = new() { Type = "http.client", Endpoint = "https://p2/new/base/" }
    } };

    [Fact]
    public async Task Existing_endpoints_are_applied_per_instance_without_mutating_connections_or_owning_services()
    {
        var preparation = Create();
        var profile = new EnvironmentProfile { Resources = new() { ["Resources:broker"] = new() { Provider = "mosquitto", Image = "unchanged:latest" } } };
        var request = new EnvironmentRunRequest(Configuration, profile) { Instances = [Instance("one"), Instance("two")] };
        var json = JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest);
        request = JsonSerializer.Deserialize(json, EnvironmentJsonContext.Default.EnvironmentRunRequest)!;
        preparation.ValidateBindings(request);
        var check = await new EnvironmentCheck(preparation, []).CheckAsync(request, CancellationToken.None);
        check.Success.ShouldBeTrue();
        check.Message.ShouldContain("Docker is not required");
        await using var prepared = await preparation.PrepareAsync(Configuration, preparation.PreparationProfile(request));
        prepared.Services.ShouldAllBe(service => service.Provider == "existing" && service.ContainerId == null);
        var first = JsonNode.Parse(preparation.BindConfiguration(prepared, request.Instances[0]))!;
        first["Resources"]!["broker"]!["Address"]!.GetValue<string>().ShouldBe("mqtts://p1:8883/");
        first["Resources"]!["broker"]!["Host"].ShouldBeNull();
        first["Resources"]!["broker"]!["Username"]!.GetValue<string>().ShouldBe("user");
        first["Resources"]!["broker"]!["Password"]!.GetValue<string>().ShouldBe("secret");
        first["Resources"]!["broker"]!["ClientId"]!.GetValue<string>().ShouldBe("Keep");
        first["Resources"]!["api"]!["BaseAddress"]!.GetValue<string>().ShouldBe("https://p2/new/base/");
        first["Resources"]!["api"]!["Headers"]!["Authorization"]!.GetValue<string>().ShouldBe("Bearer token");
        prepared.Configuration.ToJsonString().ShouldBe(JsonNode.Parse(Configuration)!.ToJsonString());
        JsonSerializer.Serialize(request, EnvironmentJsonContext.Default.EnvironmentRunRequest).ShouldBe(json);
        profile.Resources["Resources:broker"].Provider.ShouldBe("mosquitto");
        prepared.Configuration["Resources"]!["broker"]!["Username"] = "container-user";
        prepared.Configuration["Resources"]!["broker"]!["Password"] = "container-secret";
        var external = JsonNode.Parse(preparation.BindConfiguration(prepared, request.Instances[0]))!;
        external["Resources"]!["broker"]!["Username"]!.GetValue<string>().ShouldBe("user");
        external["Resources"]!["broker"]!["Password"]!.GetValue<string>().ShouldBe("secret");
        request.Instances[1].ExistingServices["Resources:api"] = new() { Type = "http.client", Endpoint = "http://other/" };
        JsonNode.Parse(preparation.BindConfiguration(prepared, request.Instances[1]))!["Resources"]!["api"]!["BaseAddress"]!.GetValue<string>().ShouldBe("http://other/");
        await prepared.DisposeAsync();
    }

    [Fact]
    public void Managed_setup_remains_when_another_instance_or_alias_still_uses_it()
    {
        var preparation = Create();
        var request = new EnvironmentRunRequest(Configuration, new() { Resources = new() { ["Resources:broker"] = new() { Provider = "mosquitto" } } })
            { Instances = [Instance("first"), new("second", "Second")] };
        preparation.PreparationProfile(request).Resources["Resources:broker"].Provider.ShouldBe("mosquitto");
        request.Instances[1].ExistingServices["Resources:broker"] = new() { Endpoint = "mqtt://p2:1883" };
        request.Instances[1].Bindings["Resources:alias"] = "Resources:broker";
        preparation.PreparationProfile(request).Resources["Resources:broker"].Provider.ShouldBe("mosquitto");
    }

    [Fact]
    public async Task Incompatible_missing_and_ambiguous_bindings_fail_before_preparation()
    {
        var instance = Instance("app");
        var request = new EnvironmentRunRequest(Configuration, new()) { Instances = [instance] };
        instance.ExistingServices["Resources:api"] = new() { Endpoint = "mqtt://wrong:1883" };
        var check = await new EnvironmentCheck(Create(), []).CheckAsync(request, CancellationToken.None);
        check.Success.ShouldBeFalse();
        check.NodeId.ShouldBe("app");
        instance.ExistingServices.Remove("Resources:api");
        instance.ExistingServices["Resources:removed"] = new();
        Should.Throw<ArgumentException>(() => Create().ValidateBindings(request));
        instance.ExistingServices.Remove("Resources:removed");
        instance.Bindings["Resources:broker"] = "Resources:broker";
        Should.Throw<ArgumentException>(() => Create().ValidateBindings(request));
    }
}
