using System.Text.Json;
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
    [Theory]
    [InlineData(false, "null")]
    [InlineData(false, "42")]
    [InlineData(true, "null")]
    [InlineData(true, "42")]
    public async Task Local_deserialization_preserves_rejection_contract(bool write, string json)
    {
        await using IFlowComponent node = write ? new FileWriteNode() : new FileReadNode();
        var output = new BufferBlock<FlowMessage>();
        var events = new BufferBlock<FlowMessage>();
        using var outputLink = node.Ports.Outputs["Output"].LinkTo(output);
        using var eventLink = node.Ports.Events!.LinkTo(events);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var input = FlowMessage.Create((FlowValue)new JsonFlowValue(document.RootElement), correlationId: CorrelationId.New());
        (await node.Ports.Inputs["Input"].SendAsync(input)).ShouldBeTrue();
        var result = await output.ReceiveAsync(TimeSpan.FromSeconds(5));
        var operation = write ? "write" : "read";
        result.Error!.Code.ShouldBe($"filesystem.{operation}.invalid_shape");
        result.Error.Category.ShouldBe("FileSystem");
        result.Error.Details!.Value.GetProperty("target").GetString()
            .ShouldBe(write ? nameof(FileContentWriteRequest) : nameof(FileReadRequest));
        result.Error.Details.Value.GetProperty("reason").GetString().ShouldNotBeNullOrWhiteSpace();
        result.CausationId.ShouldBe(input.MessageId);
        var diagnostic = (await events.ReceiveAsync(TimeSpan.FromSeconds(5))).ToFlowEvent();
        diagnostic.Name.ShouldBe($"file.{operation}.rejected");
        diagnostic.CorrelationId.ShouldBe(input.CorrelationId);
        diagnostic.Attributes["errorCode"].ShouldBe(result.Error.Code);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

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
        var writeRequest = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new FileContentWriteRequest
        {
            Path = "roundtrip.bin",
            Content = content
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: new CorrelationId("file"), traceId: new TraceId("file-trace"),
            headers: new Dictionary<string, string> { ["tenant"] = "north" });
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
        written.Value.Json.Deserialize<FileWriteResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.BytesWritten.ShouldBe(bytes.Length);
        written.CausationId.ShouldBe(writeRequest.MessageId);
        written.TraceId.ShouldBe(writeRequest.TraceId);
        written.CorrelationId.ShouldBe(writeRequest.CorrelationId);
        written.MessageId.ShouldNotBe(writeRequest.MessageId);
        written.Headers["tenant"].ShouldBe("north");
        (await File.ReadAllBytesAsync(Path.Combine(directory.Path, "roundtrip.bin"))).ShouldBe(bytes);

        var readRequest = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new FileReadRequest
        {
            Path = "roundtrip.bin",
            ReadAs = FileReadMode.Bytes,
            ContentType = content.ContentType
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web))), correlationId: writeRequest.CorrelationId, traceId: writeRequest.TraceId,
            headers: writeRequest.Headers);
        (await app.SendAsync(read.Input, readRequest)).ShouldBeTrue();
        var result = await sink.ReceiveAsync();
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));

        result.IsError.ShouldBeFalse();
        result.Value.Json.Deserialize<FileReadContent>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Content.Bytes.ToArray().ShouldBe(bytes);
        result.Value.Json.Deserialize<FileReadContent>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Content.ContentType.ShouldBe(content.ContentType);
        result.CausationId.ShouldBe(readRequest.MessageId);
        result.TraceId.ShouldBe(readRequest.TraceId);
        result.CorrelationId.ShouldBe(readRequest.CorrelationId);
        result.MessageId.ShouldNotBe(readRequest.MessageId);
        result.Headers["tenant"].ShouldBe("north");
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
