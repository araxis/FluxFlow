using FluxFlow.Composition.Addressing;
using FluxFlow.Engine;
using FluxFlow.Engine.DurableInput;
using FluxFlow.Engine.DurableInput.SqlFile;
using FluxFlow.Engine.DurableOutput;
using FluxFlow.Engine.DurableOutput.SqlFile;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var inputAddress = ApplicationAddress.WorkflowPort("Operations", "Transform", "Input");
var outputAddress = ApplicationAddress.WorkflowPort("Operations", "Transform", "Output");
var dataDirectory = Path.Combine(
    Path.GetTempPath(),
    "FluxFlow.DurabilityOperationsSample",
    Guid.NewGuid().ToString("N"));
Exception? primaryFailure = null;

try
{
    Directory.CreateDirectory(dataDirectory);
    await RunScenarioAsync(dataDirectory, inputAddress, outputAddress);
    return 0;
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

static async Task RunScenarioAsync(
    string dataDirectory,
    ApplicationAddress inputAddress,
    ApplicationAddress outputAddress)
{
    const string inputContract = "sample.input.v1";
    const string outputContract = "sample.output.v1";
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    using var telemetry = new DurabilityTelemetry();
    var deliveryHandler = new SampleOutputDeliveryHandler(SampleJsonContext.Default.String);
    var builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();

    builder.Services.AddFluxFlow(SampleWorkflow.Definition);
    SampleWorkflow.RegisterComponents(builder.Services);

    builder.Services.AddFluxFlowSqlFileDurableInput(options =>
    {
        options.DatabasePath = Path.Combine(dataDirectory, "input.db");
        options.AllowAbsoluteDatabasePath = true;
    });
    builder.Services.AddFluxFlowDurableInput(options =>
    {
        options.PollInterval = TimeSpan.FromMilliseconds(50);
        options.RetryDelay = TimeSpan.FromMilliseconds(100);
        options.StoreFailureDelay = TimeSpan.FromMilliseconds(100);
    });
    builder.Services.AddFluxFlowDurableInputContract(
        inputContract,
        SampleJsonContext.Default.String);

    builder.Services.AddFluxFlowSqlFileDurableOutput(options =>
    {
        options.DatabasePath = Path.Combine(dataDirectory, "output.db");
        options.AllowAbsoluteDatabasePath = true;
    });
    builder.Services.AddFluxFlowDurableOutput(outputs =>
        outputs.Capture(
            outputAddress,
            outputContract,
            SampleJsonContext.Default.String));
    builder.Services.AddSingleton<IDurableOutputDeliveryHandler>(deliveryHandler);
    builder.Services.AddFluxFlowDurableOutputDelivery(options =>
    {
        options.IdleDelay = TimeSpan.FromMilliseconds(50);
        options.RetryDelay = TimeSpan.FromMilliseconds(100);
    });

    using var host = builder.Build();
    var clock = host.Services.GetRequiredService<TimeProvider>();
    var inputs = host.Services.GetRequiredService<DurableApplicationInputs>();
    var inputStatusStore = host.Services.GetRequiredService<IDurableInputStatusStore>();
    var outputStatusStore = host.Services.GetRequiredService<IDurableOutputStatusStore>();

    var enqueue = await inputs.EnqueueAsync(
        inputAddress,
        FlowMessage.Create("hello durability"),
        timeout.Token);
    Ensure(enqueue.IsAccepted, "The durable input was not accepted.");

    var beforeInput = await inputStatusStore.GetStatusAsync(
        new DurableInputStatusQuery(clock.GetUtcNow()),
        timeout.Token);
    Ensure(
        beforeInput.PendingCount == 1 && beforeInput.DeliveredCount == 0,
        "The before-start input snapshot did not contain one pending message.");

    DurableInputStatusSnapshot afterInput;
    DurableOutputStatusSnapshot afterOutput;
    string deliveredValue;

    await host.StartAsync(timeout.Token);
    try
    {
        Ensure(
            host.Services.GetRequiredService<FluxFlowApplication>().State == ApplicationState.Running,
            "The FluxFlow application did not start with the host.");

        await Task.WhenAll(
                deliveryHandler.Delivered,
                telemetry.Completion)
            .WaitAsync(timeout.Token);

        deliveredValue = await deliveryHandler.Delivered.WaitAsync(timeout.Token);
        var observedAt = clock.GetUtcNow();
        afterInput = await inputStatusStore.GetStatusAsync(
            new DurableInputStatusQuery(observedAt),
            timeout.Token);
        afterOutput = await outputStatusStore.GetStatusAsync(
            new DurableOutputStatusQuery(observedAt),
            timeout.Token);

        Ensure(
            afterInput.PendingCount == 0 &&
            afterInput.LeasedCount == 0 &&
            afterInput.DeliveredCount == 1 &&
            afterInput.DeadLetteredCount == 0,
            "The final durable-input state was not delivered.");
        Ensure(
            afterOutput.PendingCount == 0 &&
            afterOutput.LeasedCount == 0 &&
            afterOutput.CompletedCount == 1 &&
            afterOutput.DeadLetteredCount == 0,
            "The final durable-output state was not completed.");
        Ensure(
            string.Equals(deliveredValue, "HELLO DURABILITY", StringComparison.Ordinal),
            "The delivery handler did not receive the transformed workflow output.");
    }
    finally
    {
        await host.StopAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    Console.WriteLine("Durability operations sample");
    Console.WriteLine(
        $"Before input status: pending={beforeInput.PendingCount} leased={beforeInput.LeasedCount} delivered={beforeInput.DeliveredCount} dead_lettered={beforeInput.DeadLetteredCount}");
    Console.WriteLine($"Delivered value: {deliveredValue}");
    Console.WriteLine(telemetry.FormatInputMetrics());
    Console.WriteLine(telemetry.FormatInputActivities());
    Console.WriteLine(telemetry.FormatOutputMetrics());
    Console.WriteLine(telemetry.FormatOutputActivities());
    Console.WriteLine(
        $"After input status: pending={afterInput.PendingCount} leased={afterInput.LeasedCount} delivered={afterInput.DeliveredCount} dead_lettered={afterInput.DeadLetteredCount}");
    Console.WriteLine(
        $"After output status: pending={afterOutput.PendingCount} leased={afterOutput.LeasedCount} completed={afterOutput.CompletedCount} dead_lettered={afterOutput.DeadLetteredCount}");
    Console.WriteLine("Status snapshots: explicit input=2 output=1; automatic polling=off");
}

static void Ensure(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
