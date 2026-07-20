using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Components.FileSystem.Tests.FileSystemTestHelpers;

namespace FluxFlow.Components.FileSystem.Tests;

public sealed class FlowContentFileSystemNodeTests
{
    [Fact]
    public async Task Read_preserves_exact_content_and_message_lineage()
    {
        using var directory = TempDirectory.Create("canonical-read");
        byte[] bytes = [0x00, 0x7F, 0xFF];
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "input.bin"), bytes);
        var timestamp = DateTimeOffset.Parse("2026-07-20T12:00:00Z");
        await using var node = new FlowContentFileReadNode(
            new FileReadOptions { BaseDirectory = directory.Path },
            new FakeTimeProvider(timestamp));
        var output = Sink(node.Output);
        var input = FlowMessage.Create(
            new FileReadRequest
            {
                Path = "input.bin",
                ReadAs = FileReadMode.Bytes,
                ContentType = "application/vnd.example.data"
            },
            new CorrelationId("file-read"),
            new TraceId("file-trace"));

        (await node.Input.SendAsync(input)).ShouldBeTrue();

        var message = await output.ReceiveAsync().WaitAsync(TestTimeout);
        var result = message.Payload;
        message.CorrelationId.ShouldBe(input.CorrelationId);
        message.TraceId.ShouldBe(input.TraceId);
        message.CausationId.ShouldBe(input.MessageId);
        message.MessageId.ShouldNotBe(input.MessageId);
        result.Kind.ShouldBe(FileSystemResultKinds.Read);
        result.IsError.ShouldBeFalse();
        result.Value.ShouldNotBeNull().ReadAt.ShouldBe(timestamp);
        result.Value.Content.OriginalBytes.AsSpan().ToArray().ShouldBe(bytes);
        result.Value.Content.ContentType.ShouldBe("application/vnd.example.data");
        result.Value.Content.Encoding.ShouldBeNull();
    }

    [Fact]
    public async Task Text_read_records_normalized_encoding_without_decoding_bytes()
    {
        using var directory = TempDirectory.Create("canonical-text-read");
        byte[] bytes = Encoding.Latin1.GetBytes("caf\u00e9");
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "input.txt"), bytes);
        await using var node = new FlowContentFileReadNode(new FileReadOptions
        {
            BaseDirectory = directory.Path,
            DefaultEncoding = "iso-8859-1"
        });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest
        {
            Path = "input.txt",
            ReadAs = FileReadMode.Text
        }));

        var content = (await output.ReceiveAsync().WaitAsync(TestTimeout))
            .Payload.Value.ShouldNotBeNull().Content;
        content.OriginalBytes.AsSpan().ToArray().ShouldBe(bytes);
        content.ContentType.ShouldBe("text/plain");
        content.Encoding.ShouldBe("iso-8859-1");
    }

    [Fact]
    public async Task Missing_read_is_normal_failure_and_later_input_continues()
    {
        using var directory = TempDirectory.Create("canonical-read-failure");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "valid.txt"), "ok");
        await using var node = new FlowContentFileReadNode(
            new FileReadOptions { BaseDirectory = directory.Path });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest { Path = "missing.txt" }));
        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest { Path = "valid.txt" }));

        var failure = (await output.ReceiveAsync().WaitAsync(TestTimeout)).Payload;
        failure.Kind.ShouldBe(FileSystemResultKinds.ReadFailed);
        failure.Error.ShouldNotBeNull().Code.ShouldBe(FileSystemErrorCodeNames.ReadNotFound);
        failure.IsError.ShouldBeTrue();

        var success = (await output.ReceiveAsync().WaitAsync(TestTimeout)).Payload;
        success.Kind.ShouldBe(FileSystemResultKinds.Read);
        success.Value.ShouldNotBeNull().Content.OriginalBytes.AsSpan().ToArray()
            .ShouldBe(Encoding.UTF8.GetBytes("ok"));
        node.Completion.IsFaulted.ShouldBeFalse();
    }

    [Fact]
    public async Task Oversized_read_is_a_normal_failure()
    {
        using var directory = TempDirectory.Create("canonical-read-limit");
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "large.bin"), [1, 2, 3]);
        await using var node = new FlowContentFileReadNode(new FileReadOptions
        {
            BaseDirectory = directory.Path,
            MaxBytes = 2
        });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new FileReadRequest
        {
            Path = "large.bin",
            ReadAs = FileReadMode.Bytes
        }));

        var result = (await output.ReceiveAsync().WaitAsync(TestTimeout)).Payload;
        result.IsError.ShouldBeTrue();
        result.Error.ShouldNotBeNull().Code.ShouldBe(FileSystemErrorCodeNames.ReadTooLarge);
    }

    [Fact]
    public async Task Write_uses_exact_original_bytes_and_preserves_lineage()
    {
        using var directory = TempDirectory.Create("canonical-write");
        byte[] bytes = [0x00, 0x7F, 0xFF];
        var timestamp = DateTimeOffset.Parse("2026-07-20T12:30:00Z");
        await using var node = new FlowContentFileWriteNode(
            new FileWriteOptions { BaseDirectory = directory.Path },
            new FakeTimeProvider(timestamp));
        var output = Sink(node.Output);
        var input = FlowMessage.Create(
            new FileContentWriteRequest
            {
                Path = "nested/output.bin",
                Content = FlowContent.FromBytes(
                    bytes,
                    "application/octet-stream")
            },
            new CorrelationId("file-write"),
            new TraceId("file-trace"));

        (await node.Input.SendAsync(input)).ShouldBeTrue();

        var message = await output.ReceiveAsync().WaitAsync(TestTimeout);
        message.CorrelationId.ShouldBe(input.CorrelationId);
        message.TraceId.ShouldBe(input.TraceId);
        message.CausationId.ShouldBe(input.MessageId);
        message.Payload.Kind.ShouldBe(FileSystemResultKinds.Written);
        message.Payload.Value.ShouldNotBeNull().WrittenAt.ShouldBe(timestamp);
        message.Payload.Value.BytesWritten.ShouldBe(bytes.Length);
        (await File.ReadAllBytesAsync(Path.Combine(directory.Path, "nested", "output.bin")))
            .ShouldBe(bytes);
    }

    [Fact]
    public async Task Value_only_write_is_normal_failure_and_later_input_continues()
    {
        using var directory = TempDirectory.Create("canonical-write-failure");
        await using var node = new FlowContentFileWriteNode(
            new FileWriteOptions { BaseDirectory = directory.Path });
        var output = Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create(new FileContentWriteRequest
        {
            Path = "invalid.txt",
            Content = FlowContent.FromValue(FlowValue.From("serialize upstream"))
        }));
        await node.Input.SendAsync(FlowMessage.Create(new FileContentWriteRequest
        {
            Path = "valid.txt",
            Content = FlowContent.FromBytes(Encoding.UTF8.GetBytes("ok"), "text/plain", "utf-8")
        }));

        var failure = (await output.ReceiveAsync().WaitAsync(TestTimeout)).Payload;
        failure.Error.ShouldNotBeNull().Code
            .ShouldBe(FileSystemErrorCodeNames.WriteContentUnavailable);
        var success = (await output.ReceiveAsync().WaitAsync(TestTimeout)).Payload;
        success.Kind.ShouldBe(FileSystemResultKinds.Written);
        (await File.ReadAllTextAsync(Path.Combine(directory.Path, "valid.txt"))).ShouldBe("ok");
    }

    [Fact]
    public async Task Directory_source_emits_flow_values_and_faults_on_source_failure()
    {
        using var directory = TempDirectory.Create("canonical-enumerate");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "entry.txt"), "entry");
        await using (var node = new FlowValueDirectoryEnumerateNode(
                         new DirectoryEnumerateOptions
                         {
                             Directory = ".",
                             BaseDirectory = directory.Path,
                             Filter = "*.txt"
                         }))
        {
            var output = Sink(node.Output);
            await node.StartAsync();
            await node.Completion.WaitAsync(TestTimeout);

            var value = (await output.ReceiveAsync().WaitAsync(TestTimeout)).Payload.GetObject();
            value["name"].GetString().ShouldBe("entry.txt");
            value["entryType"].GetString().ShouldBe(nameof(DirectoryEntryType.File));
        }

        await using var failing = new FlowValueDirectoryEnumerateNode(
            new DirectoryEnumerateOptions
            {
                Directory = "missing",
                BaseDirectory = directory.Path
            });
        await failing.StartAsync();
        await Should.ThrowAsync<IOException>(() => failing.Completion.WaitAsync(TestTimeout));
    }
}
