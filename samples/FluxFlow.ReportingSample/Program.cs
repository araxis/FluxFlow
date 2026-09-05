using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Model;
using FluxFlow.Engine;
using FluxFlow.ReportingSample;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

// Reports, persistence and presentation are consumer-owned services.
var directory = args.Length == 0
    ? Path.Combine(Path.GetTempPath(), "workflow-reports", Guid.NewGuid().ToString("N"))
    : Path.GetFullPath(args[0]);
var services = new ServiceCollection();
services.AddSingleton(new ReportArchive(directory));
services.AddSingleton<IReportArchive>(provider => provider.GetRequiredService<ReportArchive>());
services.AddApplicationReport<QuantityReport>();
var nodes = new List<SampleMetricNode>();
var registration = services.AddFluxFlow(Definition(expanded: false), options =>
{
    options.StartWithHost = false;
    options.Events = new() { Application = "reporting-example", Capacity = 256 };
});
registration.Advanced.AddDynamicComponent("sample.measure", component => component
    .UseFactory(_ => { var node = new SampleMetricNode(); nodes.Add(node); return node; })
    .HasOutput("Output", node => node.Output).HasEvents("Events", node => node.Events));
await using var provider = services.BuildServiceProvider();
var application = provider.GetRequiredService<FluxFlowApplication>();
var archive = provider.GetRequiredService<ReportArchive>();
var quantityReport = provider.GetRequiredService<QuantityReport>();
var definitions = provider.GetServices<IApplicationReport>().Where(report => report.Descriptor.Category == "measurements");
Console.WriteLine($"Discovered: {string.Join(", ", definitions.Select(report => report.Descriptor.Type))}");
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
var filter = new ApplicationReportFilter
{
    Conditions = [new ApplicationReportCondition
    {
        Path = "/measurements/value", Comparison = ApplicationReportComparison.GreaterThanOrEqual,
        Value = JsonSerializer.SerializeToElement(15)
    }]
};
var streamId = Guid.NewGuid();
long sequence = 0;
var sampleAcknowledgements = new BufferBlock<bool>();
var captureBlock = new ActionBlock<FlowMessage>(async message =>
{
    // This cursor orders observations received by this consumer, not every runtime emission.
    var record = new ApplicationReportEvent
    {
        Cursor = new(streamId, ++sequence), ObservedAt = DateTimeOffset.UtcNow, Message = message,
        Application = message.Headers.GetValueOrDefault(FlowEventHeaders.Application),
        RevisionId = message.Headers.GetValueOrDefault(FlowEventHeaders.Revision)
    };
    await archive.AppendAsync(record, timeout.Token);
    if (quantityReport.Includes(record)) sampleAcknowledgements.Post(true);
}, new ExecutionDataflowBlockOptions { BoundedCapacity = 256, CancellationToken = timeout.Token });
var displayBlock = new ActionBlock<FlowMessage>(
    message => Console.WriteLine($"Selected live: {message.MessageId}"),
    new ExecutionDataflowBlockOptions { BoundedCapacity = 256, CancellationToken = timeout.Token });
var start = new ApplicationReportCursor(streamId, 0);
using var captureLink = application.Events.LinkTo(captureBlock, new DataflowLinkOptions());
using var displayLink = application.Events.LinkTo(displayBlock, new DataflowLinkOptions(),
    message => message.Headers.GetValueOrDefault(FlowEventHeaders.Type) == "sample.measurement" &&
        message.ToFlowEvent().Measurements.GetValueOrDefault("value") >= 15);
Console.WriteLine($"Initial state: {application.State}");
await application.StartAsync(timeout.Token);
nodes[0].Measure(10);
await sampleAcknowledgements.ReceiveAsync(timeout.Token);
nodes[0].Measure(20);
await sampleAcknowledgements.ReceiveAsync(timeout.Token);
await application.ApplyAsync("revision-2", Definition(expanded: true), timeout.Token);
nodes[^1].Measure(30);
await sampleAcknowledgements.ReceiveAsync(timeout.Token);

// Freeze the consumer's observations. There is no runtime replay or completeness guarantee.
captureLink.Dispose();
displayLink.Dispose();
captureBlock.Complete();
displayBlock.Complete();
await Task.WhenAll(captureBlock.Completion, displayBlock.Completion).WaitAsync(timeout.Token);
var capture = new ReportCapture
{
    Id = Guid.NewGuid(), Start = start, End = new(streamId, sequence),
    Dropped = 0, RejectedEvents = 0, SourceCompletenessKnown = false
};
await archive.SaveCaptureAsync(capture, timeout.Token);

// Reopen history through the same concrete report, selected and injected through DI.
var reopenedReport = new QuantityReport(new ReportArchive(directory));
var result = await reopenedReport.GenerateAsync(capture.Id, filter, timeout.Token);
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Archive retained at: {directory}");

static ApplicationDefinition Definition(bool expanded) => ApplicationDefinitionJson.Deserialize(expanded
    ? """{"Resources":{},"Workflows":{"Main":{"Source":{"Type":"sample.measure"},"Extra":{"Type":"sample.measure"}}}}"""
    : """{"Resources":{},"Workflows":{"Main":{"Source":{"Type":"sample.measure"}}}}""");

sealed class SampleMetricNode : FlowNode<string, string>
{
    public void Measure(double value)
    {
        if (!EmitEvent(new FlowEvent
        {
            Name = "sample.measurement", Kind = FlowEventKind.Domain,
            Measurements = new Dictionary<string, double> { ["value"] = value }
        }.ToMessage())) throw new InvalidOperationException("Sample event was not accepted.");
    }
    protected override Task ProcessAsync(FlowMessage<string> message) => Task.CompletedTask;
}
