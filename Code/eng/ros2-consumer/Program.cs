using System.Text;
using System.Text.Json;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Ros2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rosidl.Messages.Geometry;
using RosString = Rosidl.Messages.Std.String;

[assembly: System.Runtime.CompilerServices.DisableRuntimeMarshalling]

if (args.Length is < 1 or > 2 || !args[0].StartsWith("/fluxflow_test_", StringComparison.Ordinal))
    throw new ArgumentException("Supply an isolated /fluxflow_test_<id> topic prefix.");
var prefix = args[0];
var source = new ConfigurationBuilder();
if (args.Length == 2)
{
    using var project = JsonDocument.Parse(File.ReadAllText(args[1]));
    source.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(project.RootElement.GetProperty("Configuration").GetRawText())));
}
else source.AddInMemoryCollection(
    new Dictionary<string, string?>
    {
        ["Resources:ros:Type"] = "ros.node",
        ["Resources:ros:Name"] = "fluxflow_topics",
        ["Workflows:main:receive:Type"] = "ros.string.subscriber",
        ["Workflows:main:receive:Node"] = "ros",
        ["Workflows:main:receive:Topic"] = prefix + "/input",
        ["Workflows:main:receive:Output"] = "send.Input",
        ["Workflows:main:send:Type"] = "ros.string.publisher",
        ["Workflows:main:send:Node"] = "ros",
        ["Workflows:main:send:Topic"] = prefix + "/output",
        ["Workflows:main:receiveVector:Type"] = "ros.twist.subscriber",
        ["Workflows:main:receiveVector:Node"] = "ros",
        ["Workflows:main:receiveVector:Topic"] = prefix + "/twist_input",
        ["Workflows:main:receiveVector:Output"] = "sendVector.Input",
        ["Workflows:main:sendVector:Type"] = "ros.twist.publisher",
        ["Workflows:main:sendVector:Node"] = "ros",
        ["Workflows:main:sendVector:Topic"] = prefix + "/twist_output"
    });
using var config = (ConfigurationRoot)source.Build();
// Use ROS remapping for test isolation; the saved workflow and its topic names stay unchanged.
var services = new ServiceCollection().AddRosNode(args.Length == 2
    ? ["--ros-args", "-r", "/fluxflow_input:=" + prefix + "/input", "-r", "/fluxflow_output:=" + prefix + "/output"] : null)
    .AddRosPublisher<RosString>().AddRosSubscriber<RosString>()
    .AddRosPublisher<RosString>("ros.string.publisher")
    .AddRosSubscriber<RosString>("ros.string.subscriber")
    .AddRosPublisher<Twist>("ros.twist.publisher")
    .AddRosSubscriber<Twist>("ros.twist.subscriber");
services.AddFluxFlowApplication(config);
await using var provider = services.BuildServiceProvider();
await using var app = await provider.BuildApplicationAsync();
await app.StartAsync();
Console.WriteLine("ROS_TOPICS_READY");
await Console.In.ReadLineAsync();
await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
await provider.DisposeAsync();
Console.WriteLine("ROS_TOPICS_STOPPED");
