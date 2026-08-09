using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Model;
using FluxFlow.Engine;
using FluxFlow.Engine.DurableInput;
using FluxFlow.Engine.DurableInput.SqlFile;
using FluxFlow.Engine.DurableOutput;
using FluxFlow.Engine.DurableOutput.SqlFile;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

internal static class RestartDurabilityScenario
{
    private const string InputContract = "acceptance.restart.input.v1";
    private const string WorkflowOutputContract = "acceptance.restart.output.v1";
    private const string PreappliedOutputContract = "acceptance.restart.preapplied.v1";
    private const string InputValue = "restart durability";
    private const string WorkflowOutputValue = "RESTART DURABILITY";
    private const string PreappliedOutputValue = "already applied before restart";
    private const string InputMessageId = "restart-input";
    private const string PreappliedOutputMessageId = "restart-preapplied-output";

    private static readonly DateTimeOffset SeedAt =
        new(2026, 8, 7, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LeaseUntil = SeedAt.AddMinutes(1);
    private static readonly DateTimeOffset RecoveryAt = SeedAt.AddMinutes(2);
    private static readonly ApplicationAddress InputAddress =
        ApplicationAddress.WorkflowPort("Restart", "Uppercase", "Input");
    private static readonly ApplicationAddress WorkflowOutputAddress =
        ApplicationAddress.WorkflowPort("Restart", "Uppercase", "Output");
    private static readonly ApplicationAddress PreappliedOutputAddress =
        ApplicationAddress.WorkflowPort("Restart", "External", "Output");
    private static readonly ApplicationDefinition Definition = new(
        workflows:
        [
            new("Restart", new WorkflowDefinition(
            [
                new("Uppercase", new ComponentDefinition("acceptance.restart.uppercase"))
            ]))
        ]);

    internal static async Task SeedAsync(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        EnsureFreshDirectory(dataDirectory);

        var input = CreateInputEnvelope();
        var preappliedOutput = CreatePreappliedOutputEnvelope();

        await using var provider = CreateStoreProvider(dataDirectory);
        var inputStore = provider.GetRequiredService<IDurableInputStore>();
        var outputStore = provider.GetRequiredService<IDurableOutputStore>();
        var outputDeliveryStore = provider.GetRequiredService<IDurableOutputDeliveryStore>();

        var inputEnqueue = await inputStore.EnqueueAsync(input);
        Ensure(
            inputEnqueue.Status == DurableInputEnqueueStatus.Enqueued,
            "Restart seed did not persist a new durable input.");

        var outputEnqueue = await outputStore.EnqueueAsync(preappliedOutput);
        Ensure(
            outputEnqueue.Status == DurableOutputEnqueueStatus.Enqueued,
            "Restart seed did not persist a new durable output.");

        var inputLease = (await inputStore.LeaseAsync(new DurableInputLeaseRequest(
            "restart-seed-input",
            SeedAt,
            LeaseUntil,
            maxCount: 1))).Single();
        var outputLease = await outputDeliveryStore.TryLeaseAsync(
            new DurableOutputDeliveryLeaseRequest(
                "restart-seed-output",
                SeedAt,
                LeaseUntil));

        Ensure(inputLease.Envelope.Key == input.Key, "Restart seed leased the wrong input.");
        Ensure(
            string.Equals(inputLease.OwnerId, "restart-seed-input", StringComparison.Ordinal),
            "Restart seed input lease owner changed.");
        Ensure(inputLease.LeasedAt == SeedAt, "Restart seed input lease time changed.");
        Ensure(inputLease.Attempt == 1, "Restart seed input lease did not start at attempt 1.");
        Ensure(inputLease.LeaseToken != Guid.Empty, "Restart seed input lease token was empty.");
        Ensure(
            inputLease.LeaseUntil == LeaseUntil,
            "Restart seed input lease expiry changed.");
        Ensure(outputLease is not null, "Restart seed did not lease the durable output.");
        Ensure(
            outputLease!.Envelope.Key == preappliedOutput.Key,
            "Restart seed leased the wrong output.");
        Ensure(
            string.Equals(outputLease.OwnerId, "restart-seed-output", StringComparison.Ordinal),
            "Restart seed output lease owner changed.");
        Ensure(outputLease.LeasedAt == SeedAt, "Restart seed output lease time changed.");
        Ensure(outputLease.Attempt == 1, "Restart seed output lease did not start at attempt 1.");
        Ensure(outputLease.LeaseToken != Guid.Empty, "Restart seed output lease token was empty.");
        Ensure(
            outputLease.LeaseUntil == LeaseUntil,
            "Restart seed output lease expiry changed.");

        await RestartEffectDestination.ApplyAsync(
            EffectsDirectory(dataDirectory),
            preappliedOutput,
            CancellationToken.None);

        var inputStatus = await provider
            .GetRequiredService<IDurableInputStatusStore>()
            .GetStatusAsync(new DurableInputStatusQuery(SeedAt));
        var outputStatus = await provider
            .GetRequiredService<IDurableOutputStatusStore>()
            .GetStatusAsync(new DurableOutputStatusQuery(SeedAt));

        Ensure(
            inputStatus.LeasedCount == 1 &&
            inputStatus.ExpiredLeaseCount == 0 &&
            inputStatus.DeliveredCount == 0,
            "Restart seed did not leave exactly one active input lease.");
        Ensure(
            outputStatus.LeasedCount == 1 &&
            outputStatus.ExpiredLeaseCount == 0 &&
            outputStatus.CompletedCount == 0,
            "Restart seed did not leave exactly one active output lease.");
        Ensure(
            RestartEffectDestination.Count(EffectsDirectory(dataDirectory)) == 1,
            "Restart seed did not record exactly one idempotent external effect.");

        Console.WriteLine($"PACKAGE_ACCEPTANCE_RESTART_SEED_INPUT={input.MessageId.Value}");
        Console.WriteLine($"PACKAGE_ACCEPTANCE_RESTART_SEED_OUTPUT={preappliedOutput.MessageId.Value}");
        Console.WriteLine("PACKAGE_ACCEPTANCE_RESTART_SEED_OK=True");
    }

    internal static async Task RecoverAsync(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        RequireSeedState(dataDirectory);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var effectDirectory = EffectsDirectory(dataDirectory);
        var deliveryHandler = new RestartOutputDeliveryHandler(effectDirectory);
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton<TimeProvider>(new FixedUtcTimeProvider(RecoveryAt));
        builder.Services.AddFluxFlow(Definition);
        builder.Services.AddFluxFlowComponents()
            .Advanced.AddDynamicComponent("acceptance.restart.uppercase", component =>
            {
                component
                    .UseFactory(static _ => new UppercaseNode())
                    .HasInput("Input", static node => node.Input)
                    .HasOutput("Output", static node => node.Output)
                    .HasEvents("Events", static node => node.Events);
            });

        AddStores(builder.Services, dataDirectory);
        builder.Services.AddFluxFlowDurableInput(options =>
        {
            options.LeaseDuration = TimeSpan.FromMinutes(1);
            options.PollInterval = TimeSpan.FromMilliseconds(20);
            options.RetryDelay = TimeSpan.FromMilliseconds(50);
            options.StoreFailureDelay = TimeSpan.FromMilliseconds(50);
        });
        builder.Services.AddFluxFlowDurableInputContract(
            InputContract,
            RestartJsonContext.Default.String);
        builder.Services.AddFluxFlowDurableOutput(outputs =>
            outputs.Capture(
                WorkflowOutputAddress,
                WorkflowOutputContract,
                RestartJsonContext.Default.String));
        builder.Services.AddSingleton<IDurableOutputDeliveryHandler>(deliveryHandler);
        builder.Services.AddFluxFlowDurableOutputDelivery(options =>
        {
            options.LeaseDuration = TimeSpan.FromMinutes(1);
            options.IdleDelay = TimeSpan.FromMilliseconds(20);
            options.RetryDelay = TimeSpan.FromMilliseconds(50);
        });

        using var host = builder.Build();
        var inputStatusStore = host.Services.GetRequiredService<IDurableInputStatusStore>();
        var outputStatusStore = host.Services.GetRequiredService<IDurableOutputStatusStore>();

        var beforeInput = await inputStatusStore.GetStatusAsync(
            new DurableInputStatusQuery(RecoveryAt),
            timeout.Token);
        var beforeOutput = await outputStatusStore.GetStatusAsync(
            new DurableOutputStatusQuery(RecoveryAt),
            timeout.Token);
        Ensure(
            beforeInput.LeasedCount == 1 && beforeInput.ExpiredLeaseCount == 1,
            "Restart recovery did not observe the expired input lease.");
        Ensure(
            beforeOutput.LeasedCount == 1 && beforeOutput.ExpiredLeaseCount == 1,
            "Restart recovery did not observe the expired output lease.");
        Ensure(
            RestartEffectDestination.Count(effectDirectory) == 1,
            "Restart recovery did not observe the one pre-applied effect.");

        await host.StartAsync(timeout.Token);
        try
        {
            Ensure(
                host.Services.GetRequiredService<FluxFlowApplication>().State ==
                ApplicationState.Running,
                "Restart recovery FluxFlow application did not start with the host.");

            var deliveredValue = await deliveryHandler.WorkflowOutputDelivered
                .WaitAsync(timeout.Token);
            Ensure(
                string.Equals(deliveredValue, WorkflowOutputValue, StringComparison.Ordinal),
                "Restart recovery delivered the wrong transformed workflow output.");

            var finalState = await WaitForTerminalStateAsync(
                inputStatusStore,
                outputStatusStore,
                timeout.Token);
            EnsureFinalInputStatus(finalState.Input);
            EnsureFinalOutputStatus(finalState.Output);
            EnsureExactEffects(effectDirectory);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }

        Console.WriteLine("PACKAGE_ACCEPTANCE_RESTART_INPUT_RECOVERED=True");
        Console.WriteLine("PACKAGE_ACCEPTANCE_RESTART_WORKFLOW_OUTPUT_CAPTURED=True");
        Console.WriteLine("PACKAGE_ACCEPTANCE_RESTART_PENDING_OUTPUT_RESUMED=True");
        Console.WriteLine("PACKAGE_ACCEPTANCE_RESTART_OUTPUT_RECOVERED=True");
        Console.WriteLine("PACKAGE_ACCEPTANCE_RESTART_IDEMPOTENCY_OK=True");
        Console.WriteLine("PACKAGE_ACCEPTANCE_RESTART_OK=True");
    }

    private static ServiceProvider CreateStoreProvider(string dataDirectory)
    {
        var services = new ServiceCollection();
        AddStores(services, dataDirectory);
        return services.BuildServiceProvider();
    }

    private static void AddStores(IServiceCollection services, string dataDirectory)
    {
        services.AddFluxFlowSqlFileDurableInput(options =>
        {
            options.DatabasePath = InputDatabase(dataDirectory);
            options.AllowAbsoluteDatabasePath = true;
        });
        services.AddFluxFlowSqlFileDurableOutput(options =>
        {
            options.DatabasePath = OutputDatabase(dataDirectory);
            options.AllowAbsoluteDatabasePath = true;
        });
    }

    private static DurableInputEnvelope CreateInputEnvelope()
        => new(
            InputAddress,
            InputContract,
            isError: false,
            JsonSerializer.SerializeToElement(InputValue, RestartJsonContext.Default.String),
            error: null,
            new MessageId(InputMessageId),
            new TraceId("restart-input-trace"),
            SeedAt,
            SeedAt,
            headers: new Dictionary<string, string> { ["source"] = "restart-seed" });

    private static DurableOutputEnvelope CreatePreappliedOutputEnvelope()
        => new(
            PreappliedOutputAddress,
            PreappliedOutputContract,
            isError: false,
            JsonSerializer.SerializeToElement(
                PreappliedOutputValue,
                RestartJsonContext.Default.String),
            error: null,
            new MessageId(PreappliedOutputMessageId),
            new TraceId("restart-preapplied-output-trace"),
            SeedAt,
            SeedAt,
            headers: new Dictionary<string, string> { ["source"] = "restart-seed" });

    private static async Task<(DurableInputStatusSnapshot Input, DurableOutputStatusSnapshot Output)>
        WaitForTerminalStateAsync(
            IDurableInputStatusStore inputStore,
            IDurableOutputStatusStore outputStore,
            CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = await inputStore.GetStatusAsync(
                new DurableInputStatusQuery(RecoveryAt),
                cancellationToken);
            var output = await outputStore.GetStatusAsync(
                new DurableOutputStatusQuery(RecoveryAt),
                cancellationToken);
            if (input.DeliveredCount == 1 && output.CompletedCount == 2)
                return (input, output);

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }

    private static void EnsureFinalInputStatus(DurableInputStatusSnapshot status)
        => Ensure(
            status.TotalCount == 1 &&
            status.PendingCount == 0 &&
            status.LeasedCount == 0 &&
            status.DeliveredCount == 1 &&
            status.DeadLetteredCount == 0,
            "Restart recovery input state was not exactly one delivered record.");

    private static void EnsureFinalOutputStatus(DurableOutputStatusSnapshot status)
        => Ensure(
            status.CapturedCount == 2 &&
            status.UnmaterializedCount == 0 &&
            status.PendingCount == 0 &&
            status.LeasedCount == 0 &&
            status.CompletedCount == 2 &&
            status.DeadLetteredCount == 0,
            "Restart recovery output state was not exactly two completed records.");

    private static void EnsureExactEffects(string effectDirectory)
    {
        var records = RestartEffectDestination.ReadAll(effectDirectory);
        Ensure(records.Count == 2, "Restart recovery did not retain exactly two effects.");
        Ensure(
            records.Count(record => record.Contains(
                PreappliedOutputValue,
                StringComparison.Ordinal)) == 1,
            "Restart recovery repeated or lost the pre-applied effect.");
        Ensure(
            records.Count(record => record.Contains(
                WorkflowOutputValue,
                StringComparison.Ordinal)) == 1,
            "Restart recovery repeated or lost the workflow effect.");
    }

    private static void EnsureFreshDirectory(string dataDirectory)
    {
        if (Directory.Exists(dataDirectory) &&
            Directory.EnumerateFileSystemEntries(dataDirectory).Any())
        {
            throw new InvalidOperationException(
                $"Restart seed data directory '{dataDirectory}' must be empty.");
        }

        Directory.CreateDirectory(dataDirectory);
    }

    private static void RequireSeedState(string dataDirectory)
    {
        Ensure(File.Exists(InputDatabase(dataDirectory)), "Restart input database was not found.");
        Ensure(File.Exists(OutputDatabase(dataDirectory)), "Restart output database was not found.");
        Ensure(
            RestartEffectDestination.Count(EffectsDirectory(dataDirectory)) == 1,
            "Restart seed effect evidence was not found exactly once.");
    }

    private static string InputDatabase(string dataDirectory)
        => Path.Combine(dataDirectory, "restart-input.db");

    private static string OutputDatabase(string dataDirectory)
        => Path.Combine(dataDirectory, "restart-output.db");

    private static string EffectsDirectory(string dataDirectory)
        => Path.Combine(dataDirectory, "effects");

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class FixedUtcTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

internal sealed class RestartOutputDeliveryHandler(string effectDirectory) :
    IDurableOutputDeliveryHandler
{
    private readonly TaskCompletionSource<string> _workflowOutputDelivered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task<string> WorkflowOutputDelivered => _workflowOutputDelivered.Task;

    public async ValueTask DeliverAsync(
        DurableOutputEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (envelope.IsError)
            throw new InvalidOperationException("Restart recovery expects value outputs.");

        var value = envelope.Payload.Deserialize(RestartJsonContext.Default.String)
            ?? throw new InvalidOperationException("Restart recovery output cannot be null.");
        await RestartEffectDestination.ApplyAsync(
            effectDirectory,
            envelope,
            cancellationToken);

        if (string.Equals(
                envelope.ContractName,
                "acceptance.restart.output.v1",
                StringComparison.Ordinal))
        {
            _workflowOutputDelivered.TrySetResult(value);
        }
    }
}

internal static class RestartEffectDestination
{
    internal static async Task ApplyAsync(
        string directory,
        DurableOutputEnvelope envelope,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var content = Content(envelope);
        var path = Path.Combine(directory, FileName(envelope));

        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await writer.WriteAsync(content.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }
        catch (IOException) when (File.Exists(path))
        {
            var existing = await File.ReadAllTextAsync(path, cancellationToken);
            if (!string.Equals(existing, content, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "A durable output identity was reused with different effect content.");
            }
        }
    }

    internal static int Count(string directory)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.effect", SearchOption.TopDirectoryOnly).Count()
            : 0;

    internal static IReadOnlyList<string> ReadAll(string directory)
        => Directory.EnumerateFiles(directory, "*.effect", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .ToArray();

    private static string FileName(DurableOutputEnvelope envelope)
    {
        var identity = $"{envelope.Key.Address.Value}\n{envelope.Key.MessageId.Value}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return $"{Convert.ToHexString(hash).ToLowerInvariant()}.effect";
    }

    private static string Content(DurableOutputEnvelope envelope)
        => string.Join(
            "\n",
            envelope.Key.Address.Value,
            envelope.Key.MessageId.Value,
            envelope.ContractName,
            envelope.Payload.GetRawText());
}

[JsonSerializable(typeof(string))]
internal sealed partial class RestartJsonContext : JsonSerializerContext;
