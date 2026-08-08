using System.Text.Json;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Authoring;
using FluxFlow.Composition.Model;
using FluxFlow.Data;
using FluxFlow.Engine;
using FluxFlow.Engine.HealthChecks;
using FluxFlow.Engine.DurableInput;
using FluxFlow.Engine.DurableInput.SqlFile;
using FluxFlow.Engine.DurableOutput;
using FluxFlow.Engine.DurableOutput.SqlFile;
using FluxFlow.Engine.Ports;
using FluxFlow.Fluent;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

if (args.Length > 0)
{
    if (args.Length != 2)
    {
        throw new ArgumentException(
            "Restart durability modes require exactly one absolute data-directory argument.");
    }

    if (!Path.IsPathFullyQualified(args[1]))
        throw new ArgumentException("The restart durability data directory must be absolute.");

    var restartDataDirectory = Path.GetFullPath(args[1]);
    switch (args[0])
    {
        case "durability-restart-seed":
            await RestartDurabilityScenario.SeedAsync(restartDataDirectory);
            return 0;

        case "durability-restart-recover":
            await RestartDurabilityScenario.RecoverAsync(restartDataDirectory);
            return 0;

        default:
            throw new ArgumentException($"Unknown package-consumer acceptance mode '{args[0]}'.");
    }
}

await RunEngineScenarioAsync();
Console.WriteLine("PACKAGE_ACCEPTANCE_ENGINE_OK=True");

await RunCodeFirstEngineScenarioAsync();
Console.WriteLine("PACKAGE_ACCEPTANCE_CODE_FIRST_OK=True");
Console.WriteLine("PACKAGE_ACCEPTANCE_RESOURCE_OK=True");
Console.WriteLine("PACKAGE_ACCEPTANCE_HEALTH_OK=True");

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
    const string invalidDefinitionJson = """
        {
          "Resources": {},
          "Workflows": {
            "Acceptance": {
              "Unavailable": {
                "Type": "acceptance.unavailable"
              }
            }
          }
        }
        """;

    var definition = ApplicationDefinitionJson.Deserialize(definitionJson);
    var services = new ServiceCollection();
    services.AddFluxFlow(
        definition,
        options => options.StartWithHost = false);
    services.AddFluxFlowComponents()
        .AddComponent(AcceptanceComponents.Uppercase);

    await using var provider = services.BuildServiceProvider();
    var application = provider.GetRequiredService<FluxFlowApplication>();
    var started = await application.StartAsync();
    Ensure(started.IsApplied, "The canonical package application was not applied.");

    await AssertJsonRouteAsync(application, "package-json", "PACKAGE-JSON");

    var activeRevision = application.Current;
    var activeDefinition = application.CurrentDefinition;
    Ensure(activeRevision is not null, "The canonical package application has no active revision.");
    Ensure(activeDefinition is not null, "The canonical package application has no active definition.");
    Ensure(
        ReferenceEquals(activeDefinition, definition),
        "The canonical package application did not retain the exact loaded definition.");

    var unchanged = await application.ApplyAsync("package-json-unchanged", definition);
    Ensure(
        unchanged.Status == ApplicationUpdateStatus.Unchanged,
        "Applying the unchanged JSON definition did not report unchanged.");
    Ensure(
        ReferenceEquals(unchanged.ActiveRevision, activeRevision),
        "Applying the unchanged JSON definition replaced the active revision.");
    Ensure(
        ReferenceEquals(application.Current, activeRevision),
        "Applying the unchanged JSON definition changed the current revision.");
    Ensure(
        ReferenceEquals(application.CurrentDefinition, activeDefinition),
        "Applying the unchanged JSON definition changed the current definition.");

    var invalidDefinition = ApplicationDefinitionJson.Deserialize(invalidDefinitionJson);
    var rejected = await application.ApplyAsync("package-json-invalid", invalidDefinition);
    Ensure(
        rejected.Status == ApplicationUpdateStatus.Rejected,
        "The invalid JSON candidate was not rejected.");
    Ensure(
        ReferenceEquals(rejected.ActiveRevision, activeRevision),
        "The rejected JSON candidate did not report the retained active revision.");
    Ensure(
        ReferenceEquals(application.Current, activeRevision),
        "The rejected JSON candidate changed the current revision.");
    Ensure(
        ReferenceEquals(application.CurrentDefinition, activeDefinition),
        "The rejected JSON candidate changed the current definition.");

    await AssertJsonRouteAsync(
        application,
        "package-json-after-rejection",
        "PACKAGE-JSON-AFTER-REJECTION");

    await application.StopAsync();
}

static async Task AssertJsonRouteAsync(
    FluxFlowApplication application,
    string input,
    string expectedOutput)
{
    var receive = application.Ports.ReceiveAsync<string>(
        "Acceptance.Uppercase.Output",
        TimeSpan.FromSeconds(10));
    var sent = await application.Ports.SendAsync(
        "Acceptance.Uppercase.Input",
        FlowMessage.Create(input));
    Ensure(sent.Status == PortSendStatus.Accepted, "The canonical package input was not accepted.");

    var received = await receive;
    Ensure(received.Status == PortReceiveStatus.Received, "The canonical package output was not received.");
    Ensure(
        string.Equals(received.Message?.Value, expectedOutput, StringComparison.Ordinal),
        "The canonical package output was not transformed exactly.");
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

static async Task RunCodeFirstEngineScenarioAsync()
{
    var definitionBuilder = new ApplicationDefinitionBuilder()
        .AddResource("Prefix", AcceptanceResources.Prefix, out var prefix)
        .AddWorkflow("Acceptance", out var workflow);

    Ensure(
        string.Equals(prefix.Type, AcceptanceResourceTypes.Prefix, StringComparison.Ordinal),
        "The typed code-first resource handle did not retain its exact type.");

    workflow
        .AddComponent(
            "First",
            AcceptanceComponents.Uppercase,
            out var first)
        .AddComponent(
            "Second",
            AcceptanceComponents.PrefixedUppercase,
            out var second);

    first.Output.ConnectTo(
        second.Input,
        when: static value => string.Equals(value, "PACKAGE-CODE", StringComparison.Ordinal));

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddFluxFlow(
        definitionBuilder.Build(),
        options => options.StartWithHost = false);
    services.AddHealthChecks()
        .AddFluxFlowApplication();

    await using var provider = services.BuildServiceProvider();
    var application = provider.GetRequiredService<FluxFlowApplication>();
    var started = await application.StartAsync();
    Ensure(started.IsApplied, "The typed code-first package application was not applied.");

    var receive = application.Ports.ReceiveAsync(
        second.Output,
        TimeSpan.FromSeconds(10));
    var sent = await application.Ports.SendAsync(
        first.Input,
        FlowMessage.Create("package-code"));
    Ensure(sent.Status == PortSendStatus.Accepted, "The typed code-first package input was not accepted.");

    var received = await receive;
    Ensure(received.Status == PortReceiveStatus.Received, "The typed code-first package output was not received.");
    Ensure(
        string.Equals(received.Message?.Value, "RESOURCE-PACKAGE-CODE", StringComparison.Ordinal),
        "The embedded resource contract did not affect the code-first output exactly.");

    var health = await provider
        .GetRequiredService<HealthCheckService>()
        .CheckHealthAsync(static registration =>
            string.Equals(
                registration.Name,
                "fluxflow.application",
                StringComparison.Ordinal));
    Ensure(health.Status == HealthStatus.Healthy, "The package application was not healthy.");
    Ensure(health.Entries.Count == 1, "The package application check was not registered exactly once.");
    var healthEntry = health.Entries["fluxflow.application"];
    Ensure(
        healthEntry.Tags.Order(StringComparer.Ordinal).SequenceEqual(["fluxflow", "ready"]),
        "The package application check tags changed.");
    Ensure(
        string.Equals(
            healthEntry.Data["activeRevisionId"] as string,
            application.Current?.RevisionId,
            StringComparison.Ordinal),
        "The package application check did not report the active revision exactly.");

    await application.StopAsync();
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

internal static class AcceptanceComponentTypes
{
    public const string Uppercase = "acceptance.uppercase";
}

internal static class AcceptanceComponentPorts
{
    public const string Input = "Input";
    public const string Output = "Output";
    public const string Events = "Events";
}

internal static class AcceptanceComponents
{
    public static ComponentContract<UppercaseComponentHandle> Uppercase { get; } =
        ComponentContract.Create(
            AcceptanceComponentTypes.Uppercase,
            static runtime =>
            {
                runtime
                    .UseFactory(static _ => new UppercaseNode())
                    .HasInput(AcceptanceComponentPorts.Input, static node => node.Input)
                    .HasOutput(AcceptanceComponentPorts.Output, static node => node.Output)
                    .HasEvents(AcceptanceComponentPorts.Events, static node => node.Events);
            },
            static component => new UppercaseComponentHandle(component));

    public static ComponentContract<UppercaseComponentHandle> PrefixedUppercase { get; } =
        ComponentContract.Create(
            "acceptance.prefixed-uppercase",
            static runtime =>
            {
                runtime
                    .UseFactory(static context => new PrefixedUppercaseNode(
                        context.Services.GetRequiredService<AcceptancePrefix>()))
                    .HasInput(AcceptanceComponentPorts.Input, static node => node.Input)
                    .HasOutput(AcceptanceComponentPorts.Output, static node => node.Output)
                    .HasEvents(AcceptanceComponentPorts.Events, static node => node.Events);
            },
            static component => new UppercaseComponentHandle(component));
}

internal static class AcceptanceResourceTypes
{
    public const string Prefix = "acceptance.prefix";
}

internal static class AcceptanceResources
{
    private static readonly AcceptanceResourceRegistrar Registrar = new();

    public static ApplicationResourceContract<AcceptanceResourceHandle> Prefix { get; } =
        ApplicationResourceContract.Create(
            AcceptanceResourceTypes.Prefix,
            Registrar,
            static resource => new AcceptanceResourceHandle(resource));
}

internal sealed class AcceptanceResourceHandle(ResourceHandle definition)
    : AuthoredResourceHandle(definition);

internal sealed class AcceptanceResourceRegistrar : IApplicationResourceRegistrar
{
    public void Register(ApplicationResourceRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Services.AddSingleton(new AcceptancePrefix("RESOURCE-"));
    }
}

internal sealed record AcceptancePrefix(string Value);

internal sealed class UppercaseComponentHandle(ComponentHandle definition)
    : AuthoredComponentHandle(definition)
{
    public InputPortHandle<string> Input { get; } =
        definition.Input<string>(AcceptanceComponentPorts.Input);

    public OutputPortHandle<string> Output { get; } =
        definition.Output<string>(AcceptanceComponentPorts.Output);

    public OutputPortHandle<ComponentEvent> Events { get; } =
        definition.Output<ComponentEvent>(AcceptanceComponentPorts.Events);
}

internal sealed class UppercaseNode : FlowNode<string, string>
{
    protected override async Task ProcessAsync(FlowMessage<string> message)
    {
        await EmitAsync(message.With(message.Value.ToUpperInvariant()), Stopping)
            .ConfigureAwait(false);
    }
}

internal sealed class PrefixedUppercaseNode(AcceptancePrefix prefix) : FlowNode<string, string>
{
    protected override async Task ProcessAsync(FlowMessage<string> message)
    {
        await EmitAsync(
                message.With(prefix.Value + message.Value.ToUpperInvariant()),
                Stopping)
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
