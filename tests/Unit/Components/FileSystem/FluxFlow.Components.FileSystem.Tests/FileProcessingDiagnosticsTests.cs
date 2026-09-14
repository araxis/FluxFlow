using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Nodes;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;
using static FluxFlow.Components.FileSystem.Tests.FileSystemTestHelpers;

namespace FluxFlow.Components.FileSystem.Tests;

[CollectionDefinition("File processing diagnostics", DisableParallelization = true)]
public sealed class FileDiagnosticsCollection;

[Collection("File processing diagnostics")]
public sealed class FileProcessingDiagnosticsTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task Local_failures_preserve_errors_and_continue(bool write, bool tracing, bool throwingObserver)
    {
        using var directory = TempDirectory.Create("processing-diagnostics");
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "existing.txt"), "original");
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(activityListener);
        using var observer = new Observer(throwingObserver);
        await using IFlowComponent node = write
            ? new FileWriteNode(new FileWriteOptions { BaseDirectory = directory.Path })
            : new FileReadNode(new FileReadOptions { BaseDirectory = directory.Path });
        var output = Sink(node.Ports.Outputs["Output"]);
        var bad = FlowMessage.Create(write
            ? ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new FileContentWriteRequest { Path = "existing.txt", Content = FlowContent.FromBytes(new byte[] { 1 }), Mode = FileWriteMode.CreateNew }, new JsonSerializerOptions(JsonSerializerDefaults.Web))))
            : ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new FileReadRequest { Path = "missing.txt" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))), correlationId: CorrelationId.New());
        await node.Ports.Inputs["Input"].SendAsync(bad);
        var failure = await output.ReceiveAsync(TestTimeout);
        failure.Error!.Code.ShouldBe(write ? FileSystemErrorCodeNames.WriteIoFailed : FileSystemErrorCodeNames.ReadNotFound);
        failure.Error.Category.ShouldBe("FileSystem");
        failure.CausationId.ShouldBe(bad.MessageId);
        var observed = observer.Values.ShouldHaveSingleItem();
        observed.Exception.ShouldBeAssignableTo<IOException>();
        observed.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observed.ComponentType.ShouldBe(node.GetType().FullName);
        observed.MessageId.ShouldBe(bad.MessageId);
        observed.CorrelationId.ShouldBe(bad.CorrelationId);
        observed.TraceId.ShouldBe(bad.TraceId);
        (observed.Activity is not null).ShouldBe(tracing);
        if (tracing) observed.Activity!.Status.ShouldBe(ActivityStatusCode.Error);

        await node.Ports.Inputs["Input"].SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var malformed = await output.ReceiveAsync(TestTimeout);
        malformed.Error!.Code.ShouldBe($"filesystem.{(write ? "write" : "read")}.invalid_shape");
        observer.Values.Count.ShouldBe(2);
        observer.Values[1].Exception.ShouldBeOfType<JsonException>();
        if (tracing) observer.Values[1].Activity!.Status.ShouldBe(ActivityStatusCode.Error);

        var good = FlowMessage.Create(write
            ? ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new FileContentWriteRequest { Path = "new.bin", Content = FlowContent.FromBytes(new byte[] { 1, 2 }) }, new JsonSerializerOptions(JsonSerializerDefaults.Web))))
            : ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new FileReadRequest { Path = "existing.txt" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        await node.Ports.Inputs["Input"].SendAsync(good);
        (await output.ReceiveAsync(TestTimeout)).IsError.ShouldBeFalse();
        if (write) (await File.ReadAllBytesAsync(Path.Combine(directory.Path, "new.bin"))).ShouldBe(new byte[] { 1, 2 });
        (await File.ReadAllTextAsync(Path.Combine(directory.Path, "existing.txt"))).ShouldBe("original");
        await node.Ports.Inputs["Input"].SendAsync(failure);
        (await output.ReceiveAsync(TestTimeout)).ShouldBeSameAs(failure);
        node.Complete();
        await node.Completion.WaitAsync(TestTimeout);
        observer.Values.Count.ShouldBe(2);
    }

    private sealed class Observer : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        public List<FlowProcessingException> Values { get; } = [];
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _discovery;
        private readonly bool _throws;
        public Observer(bool throws)
        {
            _throws = throws;
            _discovery = DiagnosticListener.AllListeners.Subscribe(this);
        }
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == FlowDiagnostics.ListenerName)
                _subscriptions.Add(value.Subscribe(this, name => name == FlowDiagnostics.ProcessingExceptionEvent));
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            Values.Add((FlowProcessingException)value.Value!);
            if (_throws) throw new InvalidOperationException("observer failed");
        }
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose()
        {
            _discovery.Dispose();
            foreach (var subscription in _subscriptions) subscription.Dispose();
        }
    }
}
