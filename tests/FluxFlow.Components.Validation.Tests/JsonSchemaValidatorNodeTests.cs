using FluxFlow.Components.Validation.Contracts;
using FluxFlow.Components.Validation.Diagnostics;
using FluxFlow.Components.Validation.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Validation.Tests;

public sealed class JsonSchemaValidatorNodeTests
{
    [Fact]
    public async Task JsonSchemaValidator_RoutesValidInput()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                schema = OrderSchema(),
                inputType = "json",
                boundedCapacity = 4
            });
        await runtimeNode.Node.StartAsync();
        var input = runtimeNode.FindInput(new PortName(ValidationComponentPorts.Input))
            .ShouldBeOfType<InputPort<JsonElement>>();
        var resultOutput = runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Result));
        resultOutput.ShouldNotBeNull();
        resultOutput.ValueType.ShouldBe(typeof(JsonSchemaValidationResult<JsonElement>));

        var results = new BufferBlock<JsonSchemaValidationResult<JsonElement>>();
        var valid = new BufferBlock<JsonElement>();
        resultOutput.TryLinkTo(
            new InputPort<JsonSchemaValidationResult<JsonElement>>(
                new PortAddress("test", new NodeName("results"), new PortName("Input")),
                results),
            propagateCompletion: true,
            out var resultError);
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Valid))!
            .TryLinkTo(
                new InputPort<JsonElement>(
                    new PortAddress("test", new NodeName("valid"), new PortName("Input")),
                    valid),
                propagateCompletion: true,
                out var validError);
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Invalid))!.LinkToDiscard();
        resultError.ShouldBeNull();
        validError.ShouldBeNull();

        var order = JsonSerializer.SerializeToElement(new { id = "A-100", total = 125 });
        await input.Target.SendAsync(order);
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldBeEmpty();
        result.ValueSelector.ShouldBe("input");
        (await valid.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .GetProperty("id")
            .GetString()
            .ShouldBe("A-100");
    }

    [Fact]
    public async Task JsonSchemaValidator_UsesConfiguredClockForResultTimestamp()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-02T13:00:00Z");
        var runtimeNode = CreateNode(
            options => options.UseClock(new FakeTimeProvider(timestamp)),
            new
            {
                schema = OrderSchema(),
                inputType = "json",
                boundedCapacity = 4
            });
        await runtimeNode.Node.StartAsync();
        var input = runtimeNode.FindInput(new PortName(ValidationComponentPorts.Input))
            .ShouldBeOfType<InputPort<JsonElement>>();
        var results = new BufferBlock<JsonSchemaValidationResult<JsonElement>>();
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Result))!
            .TryLinkTo(
                new InputPort<JsonSchemaValidationResult<JsonElement>>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out var resultError);
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Valid))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Invalid))!.LinkToDiscard();
        resultError.ShouldBeNull();

        await input.Target.SendAsync(JsonSerializer.SerializeToElement(new { id = "A-100", total = 125 }));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.Timestamp.ShouldBe(timestamp);
    }

    [Fact]
    public async Task JsonSchemaValidator_RoutesInvalidInputWithoutFlowError()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                schema = OrderSchema(),
                inputType = "json",
                boundedCapacity = 4
            });
        await runtimeNode.Node.StartAsync();
        var input = runtimeNode.FindInput(new PortName(ValidationComponentPorts.Input))
            .ShouldBeOfType<InputPort<JsonElement>>();
        var errors = new BufferBlock<FlowError>();
        var results = new BufferBlock<JsonSchemaValidationResult<JsonElement>>();
        var invalid = new BufferBlock<JsonElement>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Result))!
            .TryLinkTo(
                new InputPort<JsonSchemaValidationResult<JsonElement>>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    results),
                propagateCompletion: true,
                out _);
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Valid))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Invalid))!
            .TryLinkTo(
                new InputPort<JsonElement>(
                    new PortAddress("test", new NodeName("invalid"), new PortName("Input")),
                    invalid),
                propagateCompletion: true,
                out _);

        var order = JsonSerializer.SerializeToElement(new { id = "A-100", total = "wrong" });
        await input.Target.SendAsync(order);
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var result = await results.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldNotBeEmpty();
        (await invalid.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))
            .GetProperty("id")
            .GetString()
            .ShouldBe("A-100");
        errors.TryReceive(out _).ShouldBeFalse();
    }

    [Fact]
    public async Task JsonSchemaValidator_LoadsSchemaFromPath()
    {
        var schemaPath = Path.Combine(Path.GetTempPath(), $"fluxflow-schema-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(schemaPath, OrderSchema().GetRawText());
        try
        {
            var runtimeNode = CreateNode(
                _ => { },
                new
                {
                    schemaPath,
                    inputType = "string"
                });
            await runtimeNode.Node.StartAsync();
            var input = runtimeNode.FindInput(new PortName(ValidationComponentPorts.Input))
                .ShouldBeOfType<InputPort<string>>();
            var valid = new BufferBlock<string>();
            runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Result))!.LinkToDiscard();
            runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Valid))!
                .TryLinkTo(
                    new InputPort<string>(
                        new PortAddress("test", new NodeName("valid"), new PortName("Input")),
                        valid),
                    propagateCompletion: true,
                    out _);
            runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Invalid))!.LinkToDiscard();

            await input.Target.SendAsync("""{"id":"A-100","total":125}""");
            input.Target.Complete();
            await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            (await valid.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).ShouldContain("A-100");
        }
        finally
        {
            File.Delete(schemaPath);
        }
    }

    [Fact]
    public void JsonSchemaValidator_FailsBuildWhenSchemaMissing()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    inputType = "object"
                }));

        exception.Message.ShouldContain("schema");
    }

    [Fact]
    public void JsonSchemaValidator_FailsBuildWhenSchemaMalformed()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(
                _ => { },
                new
                {
                    schema = "{",
                    inputType = "object"
                }));

        exception.Message.ShouldContain("schema");
    }

    [Fact]
    public async Task JsonSchemaValidator_ReportsSelectorFailureAndContinues()
    {
        var calls = 0;
        var runtimeNode = CreateNode(
            options => options
                .RegisterType<InputMessage>("app.input")
                .UseValueSelector<InputMessage>(
                    "payload",
                    (message, _) =>
                    {
                        calls++;
                        if (calls == 1)
                        {
                            throw new InvalidOperationException("selector failed");
                        }

                        return message.Payload;
                    }),
            new
            {
                schema = OrderSchema(),
                inputType = "app.input",
                valueSelector = "payload",
                boundedCapacity = 4
            });
        await runtimeNode.Node.StartAsync();
        var input = runtimeNode.FindInput(new PortName(ValidationComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputMessage>>();
        var errors = new BufferBlock<FlowError>();
        var valid = new BufferBlock<InputMessage>();
        runtimeNode.Node.Errors.LinkTo(errors);
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Result))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Valid))!
            .TryLinkTo(
                new InputPort<InputMessage>(
                    new PortAddress("test", new NodeName("valid"), new PortName("Input")),
                    valid),
                propagateCompletion: true,
                out _);
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Invalid))!.LinkToDiscard();

        await input.Target.SendAsync(new InputMessage("""{"id":"bad","total":1}"""));
        await input.Target.SendAsync(new InputMessage("""{"id":"A-100","total":125}"""));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        error.Code.ShouldBe(ValidationErrorCodes.ValueSelectorFailed);
        (await valid.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload.ShouldContain("A-100");
    }

    [Fact]
    public async Task JsonSchemaValidator_ConvertsBytesJsonNodesAndPlainObjects()
    {
        var bytesNode = CreateNode(
            _ => { },
            new
            {
                schema = OrderSchema(),
                inputType = "bytes"
            });
        await bytesNode.Node.StartAsync();
        var byteInput = bytesNode.FindInput(new PortName(ValidationComponentPorts.Input))
            .ShouldBeOfType<InputPort<byte[]>>();
        var byteValid = new BufferBlock<byte[]>();
        bytesNode.FindOutput(new PortName(ValidationComponentPorts.Result))!.LinkToDiscard();
        bytesNode.FindOutput(new PortName(ValidationComponentPorts.Valid))!
            .TryLinkTo(
                new InputPort<byte[]>(
                    new PortAddress("test", new NodeName("bytes"), new PortName("Input")),
                    byteValid),
                propagateCompletion: true,
                out _);
        bytesNode.FindOutput(new PortName(ValidationComponentPorts.Invalid))!.LinkToDiscard();

        await byteInput.Target.SendAsync(Encoding.UTF8.GetBytes("""{"id":"A-100","total":125}"""));
        byteInput.Target.Complete();
        await bytesNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        (await byteValid.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Length.ShouldBeGreaterThan(0);

        var jsonNode = CreateNode(
            options => options.RegisterType<JsonNode>("json-node"),
            new
            {
                schema = OrderSchema(),
                inputType = "json-node"
            });
        await jsonNode.Node.StartAsync();
        var jsonNodeInput = jsonNode.FindInput(new PortName(ValidationComponentPorts.Input))
            .ShouldBeOfType<InputPort<JsonNode>>();
        var jsonNodeValid = new BufferBlock<JsonNode>();
        jsonNode.FindOutput(new PortName(ValidationComponentPorts.Result))!.LinkToDiscard();
        jsonNode.FindOutput(new PortName(ValidationComponentPorts.Valid))!
            .TryLinkTo(
                new InputPort<JsonNode>(
                    new PortAddress("test", new NodeName("jsonNodes"), new PortName("Input")),
                    jsonNodeValid),
                propagateCompletion: true,
                out _);
        jsonNode.FindOutput(new PortName(ValidationComponentPorts.Invalid))!.LinkToDiscard();

        await jsonNodeInput.Target.SendAsync(
            JsonNode.Parse("""{"id":"A-100","total":125}""")!);
        jsonNodeInput.Target.Complete();
        await jsonNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        (await jsonNodeValid.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5)))["id"]!
            .GetValue<string>()
            .ShouldBe("A-100");

        var objectNode = CreateNode(
            options => options.RegisterType<InputObject>("app.object"),
            new
            {
                schema = OrderSchema(),
                inputType = "app.object"
            });
        await objectNode.Node.StartAsync();
        var objectInput = objectNode.FindInput(new PortName(ValidationComponentPorts.Input))
            .ShouldBeOfType<InputPort<InputObject>>();
        var objectValid = new BufferBlock<InputObject>();
        objectNode.FindOutput(new PortName(ValidationComponentPorts.Result))!.LinkToDiscard();
        objectNode.FindOutput(new PortName(ValidationComponentPorts.Valid))!
            .TryLinkTo(
                new InputPort<InputObject>(
                    new PortAddress("test", new NodeName("objects"), new PortName("Input")),
                    objectValid),
                propagateCompletion: true,
                out _);
        objectNode.FindOutput(new PortName(ValidationComponentPorts.Invalid))!.LinkToDiscard();

        await objectInput.Target.SendAsync(new InputObject("A-100", 125));
        objectInput.Target.Complete();
        await objectNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        (await objectValid.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Id.ShouldBe("A-100");
    }

    [Fact]
    public async Task JsonSchemaValidator_CompletesOutputs()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                schema = OrderSchema(),
                inputType = "json",
                boundedCapacity = 4
            });
        await runtimeNode.Node.StartAsync();
        var input = runtimeNode.FindInput(new PortName(ValidationComponentPorts.Input))
            .ShouldBeOfType<InputPort<JsonElement>>();
        var resultTarget = new BufferBlock<JsonSchemaValidationResult<JsonElement>>();
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Result))!
            .TryLinkTo(
                new InputPort<JsonSchemaValidationResult<JsonElement>>(
                    new PortAddress("test", new NodeName("results"), new PortName("Input")),
                    resultTarget),
                propagateCompletion: true,
                out _);
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Valid))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Invalid))!.LinkToDiscard();

        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        // Completion propagates through the output port pump asynchronously, so
        // await it rather than checking IsCompleted synchronously.
        await resultTarget.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task JsonSchemaValidator_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(
            _ => { },
            new
            {
                schema = OrderSchema(),
                schemaId = "orders",
                inputType = "json"
            });
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(diagnostics);
        await runtimeNode.Node.StartAsync();
        var input = runtimeNode.FindInput(new PortName(ValidationComponentPorts.Input))
            .ShouldBeOfType<InputPort<JsonElement>>();
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Result))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Valid))!.LinkToDiscard();
        runtimeNode.FindOutput(new PortName(ValidationComponentPorts.Invalid))!.LinkToDiscard();

        await input.Target.SendAsync(JsonSerializer.SerializeToElement(new { id = "A-100", total = 125 }));
        input.Target.Complete();
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var loaded = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        loaded.Name.ShouldBe(ValidationDiagnosticNames.JsonSchemaLoaded);
        var valid = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        valid.Name.ShouldBe(ValidationDiagnosticNames.JsonSchemaValid);
        valid.Attributes["schemaId"].ShouldBe("orders");
    }

    private static RuntimeNode CreateNode(
        Action<ValidationComponentOptions> configure,
        object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterValidationComponents(configure);
        registry.TryGetFactory(ValidationComponentTypes.JsonSchemaValidator, out var factory).ShouldBeTrue();
        return factory(ValidationTestHost.CreateContext(configuration));
    }

    private static JsonElement OrderSchema()
        => JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "id", "total" },
            properties = new
            {
                id = new { type = "string" },
                total = new { type = "number" }
            }
        });

    private sealed record InputMessage(string Payload);

    private sealed record InputObject(string Id, decimal Total);
}
