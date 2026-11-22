using System.Text.Json;
using FluxFlow.Execution;
using FluxFlow.Plugins.Ros2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class RosPluginTests
{
    [Fact]
    public async Task Companion_checks_standard_string_workflows_without_loading_native_ROS()
    {
        var request = new EnvironmentRunRequest("""
            {"Resources":{"ros":{"Type":"ros.node","Name":"relay"}},"Workflows":{"main":{
              "receive":{"Type":"ros.subscriber","Node":"ros","Topic":"/in","Output":"send.Input"},
              "send":{"Type":"ros.publisher","Node":"ros","Topic":"/out"}}}}
            """, new());
        var original = JsonSerializer.Serialize(request);
        var services = new ServiceCollection();
        new RosPlugin().ConfigureHost(services);
        using var provider = services.BuildServiceProvider();
        var adapter = provider.GetServices<IResourceEnvironmentAdapter>().Single(item => item.Type == "ros.node");
        adapter.Endpoint(new()).ShouldBeNull();
        Should.Throw<ArgumentException>(() => adapter.Apply(new(), new Uri("http://localhost")));
        var preparation = new EnvironmentPreparation([adapter], []);
        using var logging = ExecutionLogTests.CreateFactory();
        using var plugins = RunTestPlugins.WithBasicComponents(new RosPlugin());
        using var runner = new LocalEnvironmentRunner(new WorkflowExecution(preparation, logging, plugins: plugins),
            logging.CreateLogger<LocalEnvironmentRunner>(), new(preparation, []), plugins: plugins);
        var result = await runner.CheckAsync(request, CancellationToken.None);
        result.Success.ShouldBeTrue(result.Message);
        JsonSerializer.Serialize(request).ShouldBe(original);
    }
}
