using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using FluxFlow.Engine.DurableInput;
using FluxFlow.Nodes;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.DurableInput.Tests;

public sealed class DurableInputBoundaryTests
{
    [Fact]
    public void Package_exports_a_store_protocol_but_no_built_in_provider()
    {
        var assembly = typeof(IDurableInputStore).Assembly;

        assembly.GetExportedTypes()
            .Where(type => type != typeof(IDurableInputStore) &&
                           typeof(IDurableInputStore).IsAssignableFrom(type))
            .ShouldBeEmpty();
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ShouldAllBe(name => !name.Contains("Mqtt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Package_source_has_no_reflection_dispatch_unbounded_worker_or_provider_dependency()
    {
        var packageDirectory = FindPackageDirectory();
        var sources = Directory.GetFiles(packageDirectory, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();
        sources.ShouldNotBeEmpty();
        var combined = string.Join(Environment.NewLine, sources);

        combined.ShouldNotContain("Type.GetType(");
        combined.ShouldNotContain("AssemblyQualifiedName");
        combined.ShouldNotContain("Task.Run(");
        combined.ShouldNotContain("CreateUnbounded");
        combined.ShouldNotContain("DataflowBlockOptions.Unbounded");
        combined.ShouldNotContain("exactly-once", Case.Insensitive);
        Directory.GetFiles(packageDirectory, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .ShouldAllBe(name =>
                !name!.Contains("Migration", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Provider", StringComparison.OrdinalIgnoreCase));

        var project = XDocument.Load(Path.Combine(
            packageDirectory,
            "FluxFlow.Engine.DurableInput.csproj"));
        project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .ShouldBe(["..\\FluxFlow.Engine\\FluxFlow.Engine.csproj"]);
    }

    [Fact]
    public async Task Client_and_dispatcher_logs_do_not_render_payload_or_secret_headers()
    {
        const string secretPayload = "payload-secret-29731";
        const string secretHeader = "header-secret-84106";
        var clock = new FakeTimeProvider(DurableInputTestData.Now);
        var store = new DurableInputTestStore();
        var contracts = new DurableInputContractRegistry(
        [
            new DurableInputContract<string>("text-v1", jsonTypeInfo: null)
        ]);
        var clientLogger = new RecordingLogger<DurableApplicationInputs>();
        var client = new DurableApplicationInputs(store, contracts, clock, clientLogger);
        var message = FlowMessage.Restore(
            secretPayload,
            new MessageId("logged-message"),
            new TraceId("logged-trace"),
            DurableInputTestData.Now,
            headers: new Dictionary<string, string> { ["authorization"] = secretHeader });

        var result = await client.EnqueueAsync(DurableInputTestData.Input, message);
        await using var host = await DurableInputTestApplication.CreateAsync();
        var dispatcherLogger = new RecordingLogger<DurableInputDispatcher>();
        var dispatcher = new DurableInputDispatcher(
            store,
            contracts,
            host.Application,
            DurableInputOptions.Default,
            clock,
            dispatcherLogger);
        await dispatcher.ProcessOnceAsync();

        store.Get(result.Key).State.ShouldBe(DurableInputState.Delivered);
        var logs = string.Join(
            Environment.NewLine,
            clientLogger.Messages.Concat(dispatcherLogger.Messages));
        logs.ShouldNotContain(secretPayload);
        logs.ShouldNotContain(secretHeader);
        logs.ShouldContain(message.MessageId.ToString());
        logs.ShouldContain(DurableInputTestData.Input.Value);
    }

    private static string FindPackageDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "FluxFlow.Engine.DurableInput");
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate the durable-input package source.");
    }
}
