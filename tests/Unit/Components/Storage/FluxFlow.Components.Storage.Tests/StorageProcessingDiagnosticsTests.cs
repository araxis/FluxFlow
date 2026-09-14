using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Storage.Contracts;
using FluxFlow.Components.Storage.Nodes;
using FluxFlow.Data;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Storage.Tests;

[CollectionDefinition("Storage diagnostics", DisableParallelization = true)]
public sealed class StorageDiagnosticsCollection;

[Collection("Storage diagnostics")]
public sealed class StorageProcessingDiagnosticsTests
{
    [Theory]
    [InlineData("get", false)]
    [InlineData("get", true)]
    [InlineData("put", false)]
    [InlineData("put", true)]
    [InlineData("delete", false)]
    [InlineData("delete", true)]
    [InlineData("query", false)]
    [InlineData("query", true)]
    public async Task Local_failures_report_once_and_preserve_domain_errors(string operation, bool tracing)
    {
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => tracing && source.Name == FlowDiagnostics.ListenerName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(activityListener);
        using var observer = new Observer();
        var original = new IOException("store failed", new InvalidOperationException("inner"));
        var store = new InMemoryStorageStore { FailWith = () => original };
        await using IFlowComponent node = operation switch
        {
            "get" => new StorageGetNode(store),
            "put" => new StoragePutNode(store),
            "delete" => new StorageDeleteNode(store),
            _ => new StorageQueryNode(store)
        };
        var value = operation switch
        {
            "get" => ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new StorageGetRequest { Collection = "items", Key = "a" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))),
            "put" => ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new StorageContentPutRequest { Collection = "items", Key = "a", Content = FlowContent.FromBytes(new byte[] { 1 }) }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))),
            "delete" => ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new StorageDeleteRequest { Collection = "items", Key = "a" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)))),
            _ => ((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(new StorageQueryRequest { Collection = "items" }, new JsonSerializerOptions(JsonSerializerDefaults.Web))))
        };
        var input = FlowMessage.Create(value, correlationId: CorrelationId.New());
        var output = StorageTestSink.Link(node.Ports.Outputs["Output"]);
        await node.Ports.Inputs["Input"].SendAsync(input);
        var failed = await output.ReceiveAsync(TimeSpan.FromSeconds(5));
        failed.Error!.Code.ShouldBe($"storage.{operation}_failed");
        failed.Error.Category.ShouldBe("Storage");
        failed.Error.IsTransient.ShouldBeTrue();
        failed.CausationId.ShouldBe(input.MessageId);
        var observation = observer.Values.ShouldHaveSingleItem();
        observation.Exception.ShouldBeSameAs(original);
        observation.Exception.InnerException.ShouldBeSameAs(original.InnerException);
        observation.Exception.StackTrace.ShouldNotBeNullOrWhiteSpace();
        observation.ComponentType.ShouldBe(node.GetType().FullName);
        observation.MessageId.ShouldBe(input.MessageId);
        observation.CorrelationId.ShouldBe(input.CorrelationId);
        observation.TraceId.ShouldBe(input.TraceId);
        (observation.Activity is not null).ShouldBe(tracing);
        if (tracing) observation.Activity!.Status.ShouldBe(ActivityStatusCode.Error);

        await node.Ports.Inputs["Input"].SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var invalid = await output.ReceiveAsync(TimeSpan.FromSeconds(5));
        invalid.Error!.Code.ShouldBe($"storage.{operation}.invalid_shape");
        observer.Values.Count.ShouldBe(2);
        observer.Values[1].Exception.ShouldBeOfType<JsonException>();
        if (tracing) observer.Values[1].Activity!.Status.ShouldBe(ActivityStatusCode.Error);

        store.FailWith = null;
        await node.Ports.Inputs["Input"].SendAsync(input);
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).IsError.ShouldBeFalse();
        await node.Ports.Inputs["Input"].SendAsync(failed);
        (await output.ReceiveAsync(TimeSpan.FromSeconds(5))).ShouldBeSameAs(failed);
        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        observer.Values.Count.ShouldBe(2);
    }

    private sealed class Observer : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        public List<FlowProcessingException> Values { get; } = [];
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _discovery;
        public Observer() => _discovery = DiagnosticListener.AllListeners.Subscribe(this);
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == FlowDiagnostics.ListenerName)
                _subscriptions.Add(value.Subscribe(this, name => name == FlowDiagnostics.ProcessingExceptionEvent));
        }
        public void OnNext(KeyValuePair<string, object?> value) => Values.Add((FlowProcessingException)value.Value!);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void Dispose()
        {
            _discovery.Dispose();
            foreach (var subscription in _subscriptions) subscription.Dispose();
        }
    }
}
