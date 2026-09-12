using System.Threading.Tasks.Dataflow;
using FluxFlow.Core;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.FileSystem.Tests;

public sealed class DirectComponentPortTests
{
    [Fact]
    public async Task Direct_file_graph_reads_and_writes_exact_bytes_and_forwards_errors()
    {
        using var directory = TempDirectory.Create("direct-ports");
        await using var app = new FlowApplication(linkCapacity: 1);
        var workflow = app.AddWorkflow("files");
        var writer = new FileWriteNode(new FileWriteOptions { BaseDirectory = directory.Path });
        var reader = new FileReadNode(new FileReadOptions { BaseDirectory = directory.Path });
        AssertDirectPorts(writer, writer.Input, writer.Output, writer.Events);
        AssertDirectPorts(reader, reader.Input, reader.Output, reader.Events);
        var write = workflow.Add("write", writer);
        var read = workflow.Add("read", reader);
        var sink = new DirectSink();
        var target = workflow.Add("sink", sink);
        write.Output.LinkTo(target.Input);
        read.Output.LinkTo(target.Input);
        byte[] bytes = [0, 127, 255, 0];
        var content = FlowContent.FromBytes(bytes, "application/octet-stream");
        var writeRequest = FlowMessage.Create(FlowValue.From(new FileContentWriteRequest
        {
            Path = "roundtrip.bin",
            Content = content
        }), correlationId: new CorrelationId("file"), traceId: new TraceId("file-trace"));
        await app.StartAsync();

        var error = FlowMessage.CreateError(new FlowError("upstream.failed", "Expected upstream error.", "upstream"));
        foreach (var component in new[] { write, read })
        {
            (await app.SendAsync(component.Input, error)).ShouldBeTrue();
            (await sink.ReceiveAsync()).ShouldBeSameAs(error);
        }
        (await app.SendAsync(write.Input, writeRequest)).ShouldBeTrue();
        var written = await sink.ReceiveAsync();
        written.IsError.ShouldBeFalse();
        written.Value.Deserialize<FileWriteResult>()!.BytesWritten.ShouldBe(bytes.Length);
        written.CausationId.ShouldBe(writeRequest.MessageId);
        written.TraceId.ShouldBe(writeRequest.TraceId);
        written.CorrelationId.ShouldBe(writeRequest.CorrelationId);
        (await File.ReadAllBytesAsync(Path.Combine(directory.Path, "roundtrip.bin"))).ShouldBe(bytes);

        var readRequest = FlowMessage.Create(FlowValue.From(new FileReadRequest
        {
            Path = "roundtrip.bin",
            ReadAs = FileReadMode.Bytes,
            ContentType = content.ContentType
        }), correlationId: writeRequest.CorrelationId, traceId: writeRequest.TraceId);
        (await app.SendAsync(read.Input, readRequest)).ShouldBeTrue();
        var result = await sink.ReceiveAsync();
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));

        result.IsError.ShouldBeFalse();
        result.Value.Deserialize<FileReadContent>()!.Content.Bytes.ToArray().ShouldBe(bytes);
        result.Value.Deserialize<FileReadContent>()!.Content.ContentType.ShouldBe(content.ContentType);
        result.CausationId.ShouldBe(readRequest.MessageId);
        result.TraceId.ShouldBe(readRequest.TraceId);
        result.CorrelationId.ShouldBe(readRequest.CorrelationId);
        writer.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        reader.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        app.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    private static void AssertDirectPorts(IFlowComponent component, ITargetBlock<FlowMessage> input,
        ISourceBlock<FlowMessage> output, ISourceBlock<FlowMessage> events)
    {
        component.Ports.ShouldBeSameAs(component.Ports);
        component.Ports.Inputs.ShouldHaveSingleItem().Key.ShouldBe("Input");
        component.Ports.Outputs.ShouldHaveSingleItem().Key.ShouldBe("Output");
        component.Ports.Inputs["Input"].ShouldBeSameAs(input);
        component.Ports.Outputs["Output"].ShouldBeSameAs(output);
        component.Ports.Events.ShouldBeSameAs(events);
        component.Ports.Controls.ShouldBeEmpty();
    }

    private sealed class DirectSink : IFlowComponent
    {
        private readonly BufferBlock<FlowMessage> _input = new(
            new DataflowBlockOptions { BoundedCapacity = 2 });

        public DirectSink() => Ports = new FlowPorts(
            inputs: new Dictionary<string, ITargetBlock<FlowMessage>> { ["Input"] = _input });

        public FlowPorts Ports { get; }
        public Task Completion => _input.Completion;
        public Task<FlowMessage> ReceiveAsync() => _input.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        public void Complete() => _input.Complete();
        public void Fault(Exception exception) => ((IDataflowBlock)_input).Fault(exception);
        public async ValueTask DisposeAsync() { Complete(); await Completion; }
    }
}
