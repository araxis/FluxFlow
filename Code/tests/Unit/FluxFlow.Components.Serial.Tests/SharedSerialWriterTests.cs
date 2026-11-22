using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Serial;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Serial.Tests;

public sealed class SharedSerialWriterTests
{
    [Fact]
    public async Task Partial_write_blocks_the_other_writer_and_only_fresh_input_is_sent_on_rerun()
    {
        var deadline = TimeSpan.FromSeconds(10);
        var portName = "test-" + Guid.NewGuid().ToString("N");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workflows:device:Resources:port:Type"] = "serial.port",
            ["Workflows:device:Resources:port:PortName"] = portName,
            ["Workflows:device:a:Type"] = "serial.writer", ["Workflows:device:a:Connection"] = "port",
            ["Workflows:device:b:Type"] = "serial.writer", ["Workflows:device:b:Connection"] = "port",
            ["Workflows:device:a:Framing"] = "Raw", ["Workflows:device:b:Framing"] = "Raw"
        }).Build();
        var wire = new ConcurrentQueue<byte>();
        var partial = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var timeout = new TimeoutException("Port timed out after the first byte.");
        var calls = 0;
        var opens = 0;
        var closes = 0;
        var services = new ServiceCollection().AddSerial();
        services.AddFluxFlowApplication(config);
        services.AddFluxFlowResource(ApplicationAddress.ResolveResourcePaths("port", "device")[0], _ => new SerialSession(portName, () => opens++,
            _ => throw new NotSupportedException(), bytes =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    bytes.ShouldBe(new byte[] { 0, 255 });
                    wire.Enqueue(bytes[0]);
                    partial.SetResult();
                    release.Wait(deadline).ShouldBeTrue();
                    throw timeout;
                }
                foreach (var value in bytes) wire.Enqueue(value);
            }, () => closes++));

        for (var run = 0; run < 2; run++)
        {
            var provider = services.BuildServiceProvider();
            try
            {
                var app = await provider.BuildApplicationAsync();
                await app.StartAsync().WaitAsync(deadline);
                var scope = provider.GetWorkflowServices("device");
                var a = scope.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("device.a.Input");
                var b = scope.GetRequiredKeyedService<ITargetBlock<FlowMessage>>("device.b.Input");
                var outputA = new BufferBlock<FlowMessage>();
                var outputB = new BufferBlock<FlowMessage>();
                using var linkA = scope.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("device.a.Output").LinkTo(outputA);
                using var linkB = scope.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("device.b.Output").LinkTo(outputB);
                if (run == 0)
                {
                    var second = new WriterPayload(128, 1);
                    var queued = FlowMessage.Create(second, CorrelationId.New());
                    Task stopping;
                    try
                    {
                        (await a.SendAsync(FlowMessage.Create(new WriterPayload(0, 255), CorrelationId.New()))).ShouldBeTrue();
                        await partial.Task.WaitAsync(deadline);
                        (await b.SendAsync(queued)).ShouldBeTrue();
                        await second.Processing.Task.WaitAsync(deadline);
                        calls.ShouldBe(1);
                        stopping = app.StopAsync();
                        stopping.IsCompleted.ShouldBeFalse();
                    }
                    finally { release.Set(); }
                    AssertOriginalFailure(await Record.ExceptionAsync(() => stopping.WaitAsync(deadline)));
                    (await b.SendAsync(queued)).ShouldBeFalse();
                    wire.ToArray().ShouldBe(new byte[] { 0 });
                    calls.ShouldBe(1);
                }
                else
                {
                    opens.ShouldBe(1);
                    calls.ShouldBe(1);
                    var fresh = FlowMessage.Create(new WriterPayload(0x51, 0x52), CorrelationId.New());
                    (await b.SendAsync(fresh)).ShouldBeTrue();
                    var receipt = await outputB.ReceiveAsync(deadline);
                    receipt.IsError.ShouldBeFalse();
                    receipt.MessageId.ShouldBe(fresh.MessageId);
                    receipt.TraceId.ShouldBe(fresh.TraceId);
                    receipt.CorrelationId.ShouldBe(fresh.CorrelationId);
                    await app.StopAsync().WaitAsync(deadline);
                    wire.ToArray().ShouldBe(new byte[] { 0, 0x51, 0x52 });
                    calls.ShouldBe(2);
                }
                foreach (var output in new[] { outputA, outputB })
                    while (output.TryReceive(out var message)) message.IsError.ShouldBeTrue();
                opens.ShouldBe(run + 1);
                closes.ShouldBe(opens);
            }
            finally
            {
                var disposal = await Record.ExceptionAsync(() => provider.DisposeAsync().AsTask().WaitAsync(deadline));
                if (run == 1) disposal.ShouldBeNull();
                else if (disposal is not null) AssertOriginalFailure(disposal);
            }
        }

        void AssertOriginalFailure(Exception? error)
        {
            error.ShouldNotBeNull();
            foreach (var cause in new AggregateException(error).Flatten().InnerExceptions)
                cause.GetBaseException().ShouldBeSameAs(timeout);
        }
    }

    private sealed class WriterPayload(params int[] bytes) : FlowValue
    {
        public TaskCompletionSource Processing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override JsonElement CreateJson()
        {
            var json = JsonSerializer.SerializeToElement(new { Bytes = bytes });
            Processing.TrySetResult();
            return json;
        }
    }
}
