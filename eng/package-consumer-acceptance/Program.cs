using System.Text.Json;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Model;
using FluxFlow.Data;
using FluxFlow.Engine;
using FluxFlow.Engine.DurableInput;
using FluxFlow.Engine.DurableInput.SqlFile;
using FluxFlow.Engine.DurableOutput;
using FluxFlow.Engine.DurableOutput.SqlFile;
using FluxFlow.Engine.Ports;
using FluxFlow.Fluent;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;

await RunEngineScenarioAsync();
Console.WriteLine("PACKAGE_ACCEPTANCE_ENGINE_OK=True");

await RunFluentScenarioAsync();
Console.WriteLine("PACKAGE_ACCEPTANCE_FLUENT_OK=True");

await RunDurabilityScenarioAsync();
Console.WriteLine("PACKAGE_ACCEPTANCE_DURABILITY_OK=True");

Console.WriteLine("PACKAGE_ACCEPTANCE_OK=True");

return 0;

static async Task RunEngineScenarioAsync()
{
    const string definitionJson = """
        {
          "Resources": {},
          "Workflows": {
            "Acceptance": {
              "Uppercase": {
                "Type": "acceptance.uppercase"
              }
            }
          }
        }
        """;

    var services = new ServiceCollection();
    services.AddFluxFlow(
        ApplicationDefinitionJson.Deserialize(definitionJson),
        options => options.StartWithHost = false);
    services.AddFluxFlowComponents()
        .AddRuntimeComponent("acceptance.uppercase", component =>
        {
            component.UseFactory(_ =>
            {
                var node = new UppercaseNode();
                return ValueTask.FromResult(ComponentInstance.Create(
                    node,
                    inputs: [ComponentPorts.Input<string>("Input", node.Input)],
                    outputs: [ComponentPorts.Output<string>("Output", node.Output)],
                    events: node.Events));
            });
            component.AddInput<string>("Input");
            component.AddOutput<string>("Output");
        });

    await using var provider = services.BuildServiceProvider();
    var application = provider.GetRequiredService<FluxFlowApplication>();
    var started = await application.StartAsync();
    Ensure(started.IsApplied, "The canonical package application was not applied.");

    var receive = application.Ports.ReceiveAsync<string>(
        "Acceptance.Uppercase.Output",
        TimeSpan.FromSeconds(10));
    var sent = await application.Ports.SendAsync(
        "Acceptance.Uppercase.Input",
        FlowMessage.Create("package-json"));
    Ensure(sent.Status == PortSendStatus.Accepted, "The canonical package input was not accepted.");

    var received = await receive;
    Ensure(received.Status == PortReceiveStatus.Received, "The canonical package output was not received.");
    Ensure(
        string.Equals(received.Message?.Value, "PACKAGE-JSON", StringComparison.Ordinal),
        "The canonical package output was not transformed exactly.");

    await application.StopAsync();
}

static async Task RunFluentScenarioAsync()
{
    var collector = new StringCollector();
    await using var graph = Flow
        .From(new SingleValueSource("package-fluent"))
        .Then(new UppercaseNode())
        .To(new CollectSink(collector))
        .Build();

    await graph.StartAsync();
    await graph.Completion.WaitAsync(TimeSpan.FromSeconds(10));

    Ensure(collector.Items.Count == 1, "The Fluent package graph did not produce exactly one value.");
    Ensure(
        string.Equals(collector.Items[0], "PACKAGE-FLUENT", StringComparison.Ordinal),
        "The Fluent package graph did not transform the exact value.");
}

static async Task RunDurabilityScenarioAsync()
{
    var dataDirectory = Path.Combine(
        Path.GetTempPath(),
        $"fluxflow-package-consumer-acceptance-{Guid.NewGuid():N}");
    var inputPath = Path.Combine(dataDirectory, "input.db");
    var outputPath = Path.Combine(dataDirectory, "output.db");
    Exception? primaryFailure = null;

    try
    {
        var occurredAt = new DateTimeOffset(2026, 8, 4, 8, 0, 0, TimeSpan.Zero);
        var input = new DurableInputEnvelope(
            ApplicationAddress.WorkflowPort("Acceptance", "Persist", "Input"),
            "acceptance.input.v1",
            isError: false,
            JsonSerializer.SerializeToElement(new { value = "durable-input" }),
            error: null,
            new MessageId("package-input"),
            new TraceId("package-input-trace"),
            occurredAt,
            occurredAt,
            headers: new Dictionary<string, string> { ["source"] = "package-consumer" });
        var output = new DurableOutputEnvelope(
            ApplicationAddress.WorkflowPort("Acceptance", "Persist", "Output"),
            "acceptance.output.v1",
            isError: false,
            JsonSerializer.SerializeToElement(new { value = "durable-output" }),
            error: null,
            new MessageId("package-output"),
            new TraceId("package-output-trace"),
            occurredAt,
            occurredAt,
            headers: new Dictionary<string, string> { ["source"] = "package-consumer" });

        await using (var writer = CreateDurabilityProvider(inputPath, outputPath))
        {
            var inputStore = writer.GetRequiredService<IDurableInputStore>();
            var outputStore = writer.GetRequiredService<IDurableOutputStore>();
            Ensure(
                (await inputStore.EnqueueAsync(input)).Status == DurableInputEnqueueStatus.Enqueued,
                "The package durable input was not newly persisted.");
            Ensure(
                (await outputStore.EnqueueAsync(output)).Status == DurableOutputEnqueueStatus.Enqueued,
                "The package durable output was not newly persisted.");
        }

        await using (var reader = CreateDurabilityProvider(inputPath, outputPath))
        {
            var inputStore = reader.GetRequiredService<IDurableInputStore>();
            var outputStore = reader.GetRequiredService<IDurableOutputStore>();
            var outputDelivery = reader.GetRequiredService<IDurableOutputDeliveryStore>();

            Ensure(
                (await inputStore.EnqueueAsync(input)).Status == DurableInputEnqueueStatus.AlreadyExists,
                "The reopened input store did not retain the persisted identity.");
            Ensure(
                (await outputStore.EnqueueAsync(output)).Status == DurableOutputEnqueueStatus.AlreadyExists,
                "The reopened output store did not retain the persisted identity.");

            var leaseAt = occurredAt.AddMinutes(1);
            var inputLease = (await inputStore.LeaseAsync(new DurableInputLeaseRequest(
                "package-consumer",
                leaseAt,
                leaseAt.AddMinutes(1),
                maxCount: 1))).Single();
            var outputLease = await outputDelivery.TryLeaseAsync(new DurableOutputDeliveryLeaseRequest(
                "package-consumer",
                leaseAt,
                leaseAt.AddMinutes(1)));

            Ensure(inputLease.Envelope.Key == input.Key, "The reopened input identity changed.");
            Ensure(
                string.Equals(inputLease.Envelope.ContractName, input.ContractName, StringComparison.Ordinal),
                "The reopened input contract changed.");
            Ensure(
                string.Equals(inputLease.Envelope.Payload.GetRawText(), input.Payload.GetRawText(), StringComparison.Ordinal),
                "The reopened input payload changed.");
            Ensure(outputLease is not null, "The reopened output was not available for delivery.");
            var persistedOutput = outputLease!;
            Ensure(persistedOutput.Envelope.Key == output.Key, "The reopened output identity changed.");
            Ensure(
                string.Equals(persistedOutput.Envelope.ContractName, output.ContractName, StringComparison.Ordinal),
                "The reopened output contract changed.");
            Ensure(
                string.Equals(persistedOutput.Envelope.Payload.GetRawText(), output.Payload.GetRawText(), StringComparison.Ordinal),
                "The reopened output payload changed.");
        }
    }
    catch (Exception exception)
    {
        primaryFailure = exception;
        throw;
    }
    finally
    {
        try
        {
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
        catch when (primaryFailure is not null)
        {
            // Preserve the scenario failure if cleanup also fails.
        }
    }
}

static ServiceProvider CreateDurabilityProvider(string inputPath, string outputPath)
{
    var services = new ServiceCollection();
    services.AddFluxFlowSqlFileDurableInput(options =>
    {
        options.DatabasePath = inputPath;
        options.AllowAbsoluteDatabasePath = true;
    });
    services.AddFluxFlowSqlFileDurableOutput(options =>
    {
        options.DatabasePath = outputPath;
        options.AllowAbsoluteDatabasePath = true;
    });
    return services.BuildServiceProvider();
}

static void Ensure(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

internal sealed class SingleValueSource(string value) : FlowSource<string>
{
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        await EmitAsync(FlowMessage.Create(value), cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class UppercaseNode : FlowNode<string, string>
{
    protected override async Task ProcessAsync(FlowMessage<string> message)
    {
        await EmitAsync(message.With(message.Value.ToUpperInvariant()), Stopping)
            .ConfigureAwait(false);
    }
}

internal sealed class CollectSink(StringCollector collector) : FlowNode<string, string>
{
    protected override async Task ProcessAsync(FlowMessage<string> message)
    {
        collector.Add(message.Value);
        await EmitAsync(message, Stopping).ConfigureAwait(false);
    }
}

internal sealed class StringCollector
{
    private readonly List<string> _items = [];

    public IReadOnlyList<string> Items
    {
        get
        {
            lock (_items)
            {
                return _items.ToArray();
            }
        }
    }

    public void Add(string item)
    {
        lock (_items)
        {
            _items.Add(item);
        }
    }
}
