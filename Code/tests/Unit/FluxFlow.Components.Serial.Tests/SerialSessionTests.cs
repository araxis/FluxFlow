using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Core.Messaging;
using Shouldly;
using Xunit;
using FluxFlow.Application;
using FluxFlow.Application.Addressing;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Serial;
using FluxFlow.Core.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FluxFlow.Components.Serial.Tests;

public sealed class SerialSessionTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(SerialFraming.Delimiter)]
    [InlineData(SerialFraming.FixedLength)]
    [InlineData(SerialFraming.Raw)]
    public async Task Configured_writer_loops_back_exact_bytes_and_restarts_with_fresh_framing(SerialFraming framing)
    {
        using var native = new NativePort();
        var builder = Host.CreateApplicationBuilder(); builder.Logging.ClearProviders();
        builder.Services.AddFluxFlow(); builder.Services.AddSerial();
        builder.Services.AddFluxFlowApplication(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workflows:device:Resources:port:Type"] = "serial.port",
            ["Workflows:device:Resources:port:PortName"] = "unused",
            ["Workflows:device:read:Type"] = "serial.reader", ["Workflows:device:read:Connection"] = "port",
            ["Workflows:device:write:Type"] = "serial.writer", ["Workflows:device:write:Connection"] = "port",
            ["Workflows:device:read:Framing"] = framing.ToString(), ["Workflows:device:write:Framing"] = framing.ToString(),
            ["Workflows:device:read:DelimiterBase64"] = "DQo=", ["Workflows:device:write:DelimiterBase64"] = "DQo=",
            ["Workflows:device:read:FrameLength"] = "3", ["Workflows:device:write:FrameLength"] = "3"
        }).Build());
        builder.Services.AddFluxFlowResource(ApplicationAddress.ResolveResourcePaths("port", "device")[0], _ => native.Session());
        var host = builder.Build();
        await using ((IAsyncDisposable)host)
        {
            await host.StartAsync().WaitAsync(Deadline);
            var workflow = host.Services.GetRequiredKeyedService<FlowWorkflow>("device");
            for (byte run = 1; run <= 2; run++)
            {
                if (run == 2) await workflow.StartAsync().WaitAsync(Deadline);
                var reader = (SerialReader)workflow.Services.GetRequiredKeyedService<IFlowNode>("device.read");
                var writer = (SerialWriter)workflow.Services.GetRequiredKeyedService<IFlowNode>("device.write");
                var frames = new BufferBlock<FlowMessage>(); reader.Output.LinkTo(frames);
                var receipts = new BufferBlock<FlowMessage>(); writer.Output.LinkTo(receipts);
                var firstWrite = native.Writes.Count;
                byte[] binary = [0, 255, run], text = [0xC3, 0xA9, 0x21], last = [0x51, 0x52, run];
                byte[][] payloads = framing == SerialFraming.Delimiter ? [binary, text, [], last] : [binary, text, last];
                async Task Send(SerialFrameWrite request)
                {
                    var input = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(request)));
                    (await writer.Input.SendAsync(input)).ShouldBeTrue();
                    (await receipts.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
                }
                await Send(new() { DataBase64 = Convert.ToBase64String(binary) });
                await Send(new() { Text = "\u00e9!" });
                if (framing == SerialFraming.Delimiter) await Send(new() { DataBase64 = "" });
                await Send(new() { DataBase64 = Convert.ToBase64String(last) });
                var written = native.Writes.Skip(firstWrite).ToArray();
                written.Length.ShouldBe(payloads.Length);
                for (var i = 0; i < written.Length; i++)
                {
                    byte[] expected = framing == SerialFraming.Delimiter ? [.. payloads[i], 13, 10] : payloads[i];
                    written[i].ShouldBe(expected);
                }

                // Feed only bytes actually written. Split the first frame, combine writes, and
                // deliver only the first byte of the last write before stopping the reader.
                var wire = written.SelectMany(bytes => bytes).ToArray();
                var split = framing == SerialFraming.Delimiter ? 4 : 2;
                byte[][] chunks = [wire[..split], wire[split..(wire.Length - written[^1].Length + 1)]];
                var consumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var target = native.ChunksRead + chunks.Length;
                native.BeforeRead = () => { if (native.ChunksRead == target) consumed.TrySetResult(); };
                foreach (var chunk in chunks) native.Reads.Add(chunk);
                await consumed.Task.WaitAsync(Deadline); // Reader has processed even the incomplete tail.
                foreach (var expected in framing == SerialFraming.Raw ? chunks : payloads[..^1])
                    (await frames.ReceiveAsync(Deadline)).Value.Json.GetProperty("DataBase64").GetBytesFromBase64().ShouldBe(expected);

                var finalCommand = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(new { DataBase64 = "AQID" })));
                (await writer.Input.SendAsync(finalCommand)).ShouldBeTrue();
                await workflow.StopAsync().WaitAsync(Deadline);
                frames.Count.ShouldBe(0);
                (await writer.Input.SendAsync(finalCommand)).ShouldBeFalse();
                native.Writes.Count.ShouldBe(firstWrite + payloads.Length + 1);
                native.Writes.Last().ShouldBe(framing == SerialFraming.Delimiter ? new byte[] { 1, 2, 3, 13, 10 } : new byte[] { 1, 2, 3 });
                native.Opens.ShouldBe((int)run); native.Closes.ShouldBe((int)run);
            }
            await host.StopAsync().WaitAsync(Deadline);
        }
        native.Closes.ShouldBe(2);
    }

    [Fact]
    public async Task Half_frame_is_discarded_when_a_shared_writer_times_out_and_rerun_starts_a_fresh_frame()
    {
        using var native = new NativePort();
        var services = new ServiceCollection().AddSerial();
        services.AddFluxFlowApplication(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workflows:device:Resources:port:Type"] = "serial.port",
            ["Workflows:device:Resources:port:PortName"] = "unused",
            ["Workflows:device:read:Type"] = "serial.reader", ["Workflows:device:read:Connection"] = "port",
            ["Workflows:device:read:Framing"] = "FixedLength", ["Workflows:device:read:FrameLength"] = "4",
            ["Workflows:device:write:Type"] = "serial.writer", ["Workflows:device:write:Connection"] = "port",
            ["Workflows:device:write:Framing"] = "Raw"
        }).Build());
        services.AddFluxFlowResource(ApplicationAddress.ResolveResourcePaths("port", "device")[0], _ => native.Session());

        var timeout = new TimeoutException("Serial write timed out after one byte.");
        native.BeforeWrite = bytes =>
        {
            bytes.ShouldBe(new byte[] { 0, 255 });
            native.Writes.Enqueue(bytes[..1]);
            throw timeout;
        };
        for (var run = 1; run <= 2; run++)
        {
            var halfBuffered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstChunk = native.ChunksRead + 1;
            native.BeforeRead = () => { if (native.ChunksRead == firstChunk) halfBuffered.TrySetResult(); };
            var provider = services.BuildServiceProvider();
            try
            {
                var app = await provider.BuildApplicationAsync();
                await app.StartAsync().WaitAsync(Deadline);
                var scope = provider.GetWorkflowServices("device");
                var frames = new BufferBlock<FlowMessage>();
                using var link = scope.GetRequiredKeyedService<ISourceBlock<FlowMessage>>("device.read.Output").LinkTo(frames);
                native.Reads.Add(run == 1 ? [0x11, 0x22] : [0x33, 0x44]);
                await halfBuffered.Task.WaitAsync(Deadline);
                frames.Count.ShouldBe(0);
                if (run == 1)
                {
                    var writer = (SerialWriter)scope.GetRequiredKeyedService<IFlowNode>("device.write");
                    var input = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(new { Bytes = new[] { 0, 255 } })));
                    (await writer.Input.SendAsync(input)).ShouldBeTrue();
                    AssertTimeout(await Record.ExceptionAsync(() => writer.Completion.WaitAsync(Deadline)));
                    AssertTimeout(await Record.ExceptionAsync(() => app.StopAsync().WaitAsync(Deadline)));
                }
                else
                {
                    native.Reads.Add([0x55, 0x66]);
                    var frame = await frames.ReceiveAsync(Deadline);
                    frame.Value.Json.GetProperty("DataBase64").GetBytesFromBase64().ShouldBe(new byte[] { 0x33, 0x44, 0x55, 0x66 });
                    await app.StopAsync().WaitAsync(Deadline);
                }
                frames.Count.ShouldBe(0);
                native.Closes.ShouldBe(run);
            }
            finally
            {
                var disposal = await Record.ExceptionAsync(() => provider.DisposeAsync().AsTask().WaitAsync(Deadline));
                if (run == 1 && disposal is not null) AssertTimeout(disposal);
                else disposal.ShouldBeNull();
            }
            native.Opens.ShouldBe(run);
            native.Closes.ShouldBe(run);
            native.WriteCalls.ShouldBe(1);
            native.Writes.Single().ShouldBe(new byte[] { 0 });
        }

        void AssertTimeout(Exception? failure)
        {
            failure.ShouldNotBeNull();
            foreach (var cause in new AggregateException(failure).Flatten().InnerExceptions)
                cause.GetBaseException().ShouldBeSameAs(timeout);
        }
    }

    [Fact]
    public void Delimited_and_fixed_frames_survive_fragmentation_and_preserve_original_bytes()
    {
        var decoder = new SerialFrameDecoder(new() { DelimiterBase64 = "DQo=", MaxFrameBytes = 4, ReadBufferBytes = 4 });
        decoder.Push([0, 255, 13], 3).ShouldBeEmpty();
        decoder.Push([10, 13, 10, 1], 4).Select(Convert.ToBase64String).ShouldBe(new[] { "AP8=", "" });
        decoder.Push([2, 13, 10], 3).Single().ShouldBe(new byte[] { 1, 2 });
        var fixedFrames = new SerialFrameDecoder(new() { Framing = SerialFraming.FixedLength, FrameLength = 3 });
        fixedFrames.Push([1, 2], 2).ShouldBeEmpty();
        fixedFrames.Push([3, 4, 5, 6, 7], 5).Select(Convert.ToBase64String).ShouldBe(new[] { "AQID", "BAUG" });
    }

    [Fact]
    public void Oversize_frames_fault_instead_of_unbounded_buffering_or_silent_truncation()
    {
        var decoder = new SerialFrameDecoder(new() { MaxFrameBytes = 2, ReadBufferBytes = 2 });
        Should.Throw<InvalidDataException>(() => decoder.Push([1, 2, 3, 4], 4).ToArray());
    }

    [Fact]
    public async Task Reader_and_writers_share_one_open_and_preserve_bytes_on_decoding_failure()
    {
        using var native = new NativePort();
        await using var session = native.Session();
        await using var reader = new SerialReader(session);
        await using var writer = new SerialWriter(session);
        var reads = new BufferBlock<FlowMessage>(); var writes = new BufferBlock<FlowMessage>();
        reader.Output.LinkTo(reads); writer.Output.LinkTo(writes);
        await reader.StartAsync().WaitAsync(Deadline);
        native.Reads.Add([0xff, 10]);
        var frame = (await reads.ReceiveAsync(Deadline)).Value.Json;
        frame.GetProperty("DataBase64").GetString().ShouldBe("/w==");
        frame.GetProperty("Text").ValueKind.ShouldBe(JsonValueKind.Null);
        frame.GetProperty("DecodingError").GetString().ShouldNotBeNullOrWhiteSpace();
        var request = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(new { Text = "command" })));
        (await writer.Input.SendAsync(request)).ShouldBeTrue();
        await writes.ReceiveAsync(Deadline);
        native.Writes.Single().ShouldBe(Encoding.UTF8.GetBytes("command\n"));
        native.Opens.ShouldBe(1);
        reader.StopProduction(); writer.Input.Complete();
        await Task.WhenAll(reader.Completion, writer.Completion).WaitAsync(Deadline);
        native.Closes.ShouldBe(0);
        await session.DisposeAsync(); native.Closes.ShouldBe(1);
        (await writer.Input.SendAsync(request)).ShouldBeFalse();
    }

    [Fact]
    public async Task Writes_are_serialized_and_disposal_waits_for_outstanding_native_io()
    {
        using var native = new NativePort();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        native.BeforeWrite = _ => { entered.Set(); release.Wait(Deadline).ShouldBeTrue(); };
        await using var session = native.Session();
        var first = session.WriteAsync([1], CancellationToken.None);
        entered.Wait(Deadline).ShouldBeTrue();
        using var cancel = new CancellationTokenSource();
        var queued = session.WriteAsync([2], cancel.Token); cancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => queued);
        var stop = session.DisposeAsync().AsTask();
        stop.IsCompleted.ShouldBeFalse(); native.Closes.ShouldBe(0);
        release.Set(); await Task.WhenAll(first, stop).WaitAsync(Deadline);
        native.Writes.Count.ShouldBe(1); native.Closes.ShouldBe(1);
        await Should.ThrowAsync<ObjectDisposedException>(() => session.WriteAsync([3], CancellationToken.None));
    }

    [Fact]
    public async Task Failed_partial_write_is_not_retried_and_blocks_following_commands()
    {
        using var native = new NativePort();
        native.BeforeWrite = _ => throw new TimeoutException("partial write");
        await using var session = native.Session();
        await Should.ThrowAsync<TimeoutException>(() => session.WriteAsync([1], CancellationToken.None));
        var failure = await Should.ThrowAsync<IOException>(() => session.WriteAsync([2], CancellationToken.None));
        failure.InnerException.ShouldBeOfType<TimeoutException>();
        native.WriteCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Disconnect_preserves_the_original_failure_and_prevents_further_commands()
    {
        using var native = new NativePort();
        await using var session = native.Session();
        await session.OpenAsync(CancellationToken.None);
        var disconnected = new IOException("Device unplugged during read.");
        native.BeforeRead = () => throw disconnected;
        Should.Throw<IOException>(() => session.Read(new byte[16], CancellationToken.None)).ShouldBeSameAs(disconnected);
        var failure = await Should.ThrowAsync<IOException>(() => session.WriteAsync([1], CancellationToken.None));
        failure.InnerException.ShouldBeSameAs(disconnected);
        native.WriteCalls.ShouldBe(0);
        await session.DisposeAsync();
        native.Closes.ShouldBe(1);
        native.BeforeRead = null;
        await using var rerun = native.Session();
        await rerun.WriteAsync([2], CancellationToken.None);
        native.Writes.Single().ShouldBe(new byte[] { 2 });
    }

    [Fact]
    public async Task Access_denied_during_startup_releases_ownership_without_claiming_an_open_port()
    {
        var name = "denied-" + Guid.NewGuid().ToString("N");
        var denied = new UnauthorizedAccessException("Port access denied.");
        var closes = 0;
        await using var failed = new SerialSession(name, () => throw denied, _ => 0, _ => { }, () => closes++);
        (await Should.ThrowAsync<UnauthorizedAccessException>(() => failed.OpenAsync(CancellationToken.None))).ShouldBeSameAs(denied);
        await failed.DisposeAsync();
        closes.ShouldBe(0);
        await using var nextRun = new SerialSession(name, () => { }, _ => 0, _ => { }, () => closes++);
        await nextRun.OpenAsync(CancellationToken.None);
        await nextRun.DisposeAsync();
        closes.ShouldBe(1);
    }

    [Fact]
    public async Task End_of_stream_faults_the_shared_session_until_a_fresh_run()
    {
        using var native = new NativePort();
        await using var session = native.Session();
        var reader = new SerialReader(session);
        try
        {
            await reader.StartAsync().WaitAsync(Deadline);
            native.Reads.Add([]);
            var eof = await Should.ThrowAsync<EndOfStreamException>(() => reader.ProductionCompletion.WaitAsync(Deadline));
            var refused = await Should.ThrowAsync<IOException>(() => session.WriteAsync([1], default));
            refused.InnerException.ShouldBeSameAs(eof);
            native.WriteCalls.ShouldBe(0);
        }
        finally { await reader.DisposeAsync(); }
        await session.DisposeAsync();
        await using var next = native.Session();
        await next.WriteAsync([2], default);
        native.Writes.Single().ShouldBe(new byte[] { 2 });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_native_open_does_not_leave_a_source_reading(bool duplex)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var reads = 0;
        var closes = 0;
        void Open() { entered.Set(); release.Wait(Deadline).ShouldBeTrue(); }
        int Read(byte[] _) { Interlocked.Increment(ref reads); throw new TimeoutException(); }
        void Close() => Interlocked.Increment(ref closes);
        await using var session = new SerialSession(Guid.NewGuid().ToString(), Open, Read, _ => { }, Close);
        await using FlowNode node = duplex ? new SerialText(new(), Open, Read, _ => { }, Close) : new SerialReader(session);
        var source = (IFlowSource)node;
        var starting = source.StartAsync(cancellation.Token);
        try
        {
            entered.Wait(Deadline).ShouldBeTrue();
            cancellation.Cancel();
            release.Set();
            await Should.ThrowAsync<OperationCanceledException>(() => starting.WaitAsync(Deadline));
            await source.ProductionCompletion.WaitAsync(Deadline);
            reads.ShouldBe(0);
        }
        finally { release.Set(); source.StopProduction(); await node.DisposeAsync(); }
        await session.DisposeAsync();
        closes.ShouldBe(1);
    }

    [Fact]
    public async Task Bytes_round_trip_as_numbers_and_invalid_values_never_reach_the_port()
    {
        using var native = new NativePort();
        await using var session = native.Session();
        var options = new SerialFrameOptions { Framing = SerialFraming.Raw, OutputFormat = SerialFrameOutputFormat.Bytes,
            MaxFrameBytes = 4, ReadBufferBytes = 4 };
        await using var reader = new SerialReader(session, options);
        await using var writer = new SerialWriter(session, options);
        var frames = new BufferBlock<FlowMessage>(); reader.Output.LinkTo(frames);
        var receipts = new BufferBlock<FlowMessage>(); writer.Output.LinkTo(receipts);
        await reader.StartAsync().WaitAsync(Deadline);
        async Task<FlowMessage> Send(string json)
        {
            using var document = JsonDocument.Parse(json);
            await writer.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(document.RootElement.Clone())));
            return await receipts.ReceiveAsync(Deadline);
        }
        foreach (var bad in new[] { "-1", "256", "1.5", "\"1\"", "null" })
        {
            var rejected = await Send("{\"Bytes\":[1," + bad + "]}");
            rejected.IsError.ShouldBeTrue();
            rejected.Error!.Json.ToString().ShouldContain("Bytes[1]");
        }
        foreach (var bad in new[] { """{"Bytes":[0],"Text":"a"}""", """{"Bytes":[],"DataBase64":""}""",
            """{"Bytes":[0,1,2,3,4]}""", """{"Bytes":"AP8="}""" })
            (await Send(bad)).IsError.ShouldBeTrue();
        native.WriteCalls.ShouldBe(0);
        (await Send("""{"Bytes":[255,0,128,10]}""")).IsError.ShouldBeFalse();
        var sent = native.Writes.Single();
        sent.ShouldBe(new byte[] { 255, 0, 128, 10 });
        native.Reads.Add(sent);
        var frame = (await frames.ReceiveAsync(Deadline)).Value.Json;
        frame.GetProperty("Bytes").EnumerateArray().Select(n => n.GetByte()).ToArray().ShouldBe(sent);
        frame.TryGetProperty("DataBase64", out _).ShouldBeFalse();
        frame.GetProperty("DecodingError").GetString().ShouldNotBeNullOrWhiteSpace();
        reader.StopProduction(); writer.Input.Complete();
        await Task.WhenAll(reader.Completion, writer.Completion).WaitAsync(Deadline);
    }

    [Fact]
    public async Task Overlapping_delimiter_cannot_silently_change_the_frame_sent_to_a_reader()
    {
        using var native = new NativePort();
        await using var session = native.Session();
        var options = new SerialFrameOptions { DelimiterBase64 = "fHw=" }; // ||
        await using var writer = new SerialWriter(session, options);
        var replies = new BufferBlock<FlowMessage>(); writer.Output.LinkTo(replies);
        var ambiguous = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(new { Text = "a|" })));
        await writer.Input.SendAsync(ambiguous);
        (await replies.ReceiveAsync(Deadline)).IsError.ShouldBeTrue();
        native.WriteCalls.ShouldBe(0);
        var valid = FlowMessage.Create(new JsonFlowValue(JsonSerializer.SerializeToElement(new { Text = "|a" })));
        await writer.Input.SendAsync(valid);
        (await replies.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        var sent = native.Writes.Single();
        new SerialFrameDecoder(options).Push(sent, sent.Length).Single().ShouldBe("|a"u8.ToArray());
        writer.Input.Complete(); await writer.Completion.WaitAsync(Deadline);
    }

    [Fact]
    public async Task Ownership_is_exclusive_until_confirmed_close_and_a_new_run_can_acquire_it()
    {
        using var native = new NativePort();
        await using var first = native.Session(); await using var second = native.Session();
        await first.OpenAsync(CancellationToken.None);
        await Should.ThrowAsync<IOException>(() => second.OpenAsync(CancellationToken.None));
        native.CloseFailure = true;
        await Should.ThrowAsync<IOException>(() => first.DisposeAsync().AsTask());
        await using var third = native.Session();
        await Should.ThrowAsync<IOException>(() => third.OpenAsync(CancellationToken.None));
        native.CloseFailure = false; await first.DisposeAsync();
        await third.OpenAsync(CancellationToken.None); native.Opens.ShouldBe(2);
    }

    [Fact]
    public async Task Cancellation_before_start_does_not_open_a_port_and_second_reader_is_rejected()
    {
        using var native = new NativePort(); await using var session = native.Session();
        await Should.ThrowAsync<OperationCanceledException>(() => session.OpenAsync(new CancellationToken(true)));
        native.Opens.ShouldBe(0);
        using var owner = session.ClaimReader();
        Should.Throw<InvalidOperationException>(() => session.ClaimReader());
    }

    [Fact]
    public async Task Stop_cancels_a_reader_blocked_by_full_output_without_late_frames()
    {
        using var native = new NativePort(); await using var session = native.Session();
        await using var reader = new SerialReader(session, new() { Capacity = 1 });
        await reader.StartAsync(); native.Reads.Add([1, 10, 2, 10, 3, 10]);
        (await reader.Output.ReceiveAsync(Deadline)).Value.Json.GetProperty("DataBase64").GetString().ShouldBe("AQ==");
        reader.StopProduction();
        await reader.ProductionCompletion.WaitAsync(Deadline);
        reader.Output.LinkTo(DataflowBlock.NullTarget<FlowMessage>());
        await reader.Completion.WaitAsync(Deadline);
        await session.DisposeAsync(); native.Closes.ShouldBe(1);
    }

    private sealed class NativePort : IDisposable
    {
        private readonly string _name = "test-" + Guid.NewGuid().ToString("N");
        public BlockingCollection<byte[]> Reads { get; } = new();
        public ConcurrentQueue<byte[]> Writes { get; } = new();
        public int Opens, Closes, WriteCalls, ChunksRead;
        public bool CloseFailure;
        public Action<byte[]>? BeforeWrite;
        public Action? BeforeRead;
        public SerialSession Session() => new(_name, () => Opens++, buffer =>
        {
            BeforeRead?.Invoke();
            if (!Reads.TryTake(out var bytes, 20)) throw new TimeoutException();
            bytes.CopyTo(buffer, 0); Interlocked.Increment(ref ChunksRead); return bytes.Length;
        }, bytes => { Interlocked.Increment(ref WriteCalls); BeforeWrite?.Invoke(bytes); Writes.Enqueue(bytes.ToArray()); }, () =>
        { if (CloseFailure) throw new IOException("Close unconfirmed"); Closes++; });
        public void Dispose() => Reads.Dispose();
    }
}
