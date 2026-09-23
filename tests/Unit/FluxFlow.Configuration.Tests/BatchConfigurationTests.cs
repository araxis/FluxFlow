using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Batch;
using FluxFlow.Configuration.Batching;
using FluxFlow.Core.Application;
using FluxFlow.Core.Messaging;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;

public sealed class BatchConfigurationTests
{
    [Theory]
    [InlineData("size")]
    [InlineData("time")]
    [InlineData("stop")]
    public async Task Configured_batch_preserves_messages_and_flushes(string trigger)
    {
        using var config = Read(2, "00:00:05", 8);
        var time = new FakeTimeProvider();
        var constructions = 0;
        var services = new ServiceCollection().AddBatch();
        services.AddSingleton<TimeProvider>(_ => { constructions++; return time; });
        services.AddKeyedSingleton<ComponentRegistration>("sink", (registrations, section) =>
            registrations.AddComponentFactory(section.GetComponentAddress(), _ => new Sink())
                .AddInputPort<Sink>(section.GetComponentAddress(), "Input", node => node.Input));
        await services.AddFluxFlowApplicationAsync(config);
        config["workflows:main:collect:BatchSize"] = "100";
        config["workflows:main:collect:MaxWait"] = "01:00:00";
        constructions.ShouldBe(0);
        await using var provider = services.BuildServiceProvider();
        constructions.ShouldBe(0);
        await using var app = await provider.BuildApplicationAsync();
        constructions.ShouldBe(1);
        var batch = provider.GetRequiredKeyedService<IFlowNode>("main.collect").ShouldBeOfType<Batch>();
        provider.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("main.collect.Input").ShouldBeSameAs(batch.Input);
        provider.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.collect.Output").ShouldBeSameAs(batch.Output);
        provider.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("main.collect.Events").ShouldNotBeNull();
        var sink = provider.GetRequiredKeyedService<IFlowNode>("main.sink").ShouldBeOfType<Sink>();
        await app.StartAsync();
        var first = FlowMessage.Create((FlowValue)"one");
        await batch.Input.SendAsync(first);
        // An error passes through after the first input, without entering the batch.
        var marker = FlowMessage.Create((FlowValue)new FlowError("marker", "marker", "test"));
        await batch.Input.SendAsync(marker);
        (await sink.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).ShouldBeSameAs(marker);
        (await provider.BuildApplicationAsync()).ShouldBeSameAs(app);
        constructions.ShouldBe(1);
        FlowMessage? second = null;
        if (trigger == "size")
        {
            second = FlowMessage.Create((FlowValue)"two");
            await batch.Input.SendAsync(second);
        }
        else if (trigger == "time") time.Advance(TimeSpan.FromSeconds(5));
        else await app.StopAsync();
        var result = (await sink.Received.ReceiveAsync(TimeSpan.FromSeconds(10))).Value.ShouldBeOfType<FlowBatch>();
        result.Items.Count.ShouldBe(second is null ? 1 : 2);
        result.Items[0].ShouldBeSameAs(first);
        if (second is not null) result.Items[1].ShouldBeSameAs(second);
        await app.StopAsync();
        sink.Received.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(0, "00:00:05", 128)]
    [InlineData(-1, "00:00:05", 128)]
    [InlineData(2, null, 128)]
    [InlineData(2, "00:00:00", 128)]
    [InlineData(2, "-00:00:01", 128)]
    [InlineData(2, "60.00:00:00", 128)]
    [InlineData(2, "invalid", 128)]
    [InlineData(2, "00:00:05", 0)]
    public async Task Invalid_settings_reject_registration_atomically(int size, string? wait, int capacity)
    {
        using var config = Read(size, wait, capacity);
        var services = new ServiceCollection().AddBatch();
        var before = services.ToArray();
        var error = await Should.ThrowAsync<InvalidOperationException>(async () => await services.AddFluxFlowApplicationAsync(config));
        error.Message.ShouldContain("main.collect");
        services.ToArray().ShouldBe(before);
    }

    private static ConfigurationRoot Read(int size, string? wait, int capacity)
        => (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["workflows:main:collect:Type"] = "batch",
            ["workflows:main:collect:BatchSize"] = size.ToString(),
            ["workflows:main:collect:MaxWait"] = wait,
            ["workflows:main:collect:OutputCapacity"] = capacity.ToString(),
            ["workflows:main:collect:Output"] = "sink.Input",
            ["workflows:main:sink:Type"] = "sink"
        }).Build();

    private sealed class Sink : FlowTransformNode
    {
        public BufferBlock<FlowMessage> Received { get; } = new();
        protected override bool HandlesErrors => true;
        protected override Task<IEnumerable<FlowMessage>> TransformAsync(FlowMessage message)
        {
            Received.Post(message);
            return Task.FromResult<IEnumerable<FlowMessage>>([]);
        }
    }
}
