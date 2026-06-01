using FluxFlow.Components.Payloads.Contracts;
using FluxFlow.Components.Payloads.Diagnostics;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Definitions;
using FluxFlow.Engine.Runtime;
using Shouldly;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Payloads.Tests;

public sealed class PayloadInspectNodeTests
{
    [Fact]
    public async Task Inspect_ClassifiesJsonObject()
    {
        var runtimeNode = CreateNode(new { maxPreviewBytes = 128 });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<PayloadInspectionResult>(
            runtimeNode,
            PayloadComponentPorts.Output);

        await input.Target.SendAsync(new PayloadInspectionRequest
        {
            Text = """{"name":"flux","count":2}""",
            ContentType = "application/json"
        });
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Kind.ShouldBe(PayloadKind.JsonObject);
        result.ByteCount.ShouldBeGreaterThan(0);
        result.ContentType.ShouldBe("application/json");
        result.DetectedEncoding.ShouldBe("utf-8");
        result.TextPreview.ShouldNotBeNull();
        result.TextPreview.ShouldContain("\"name\"");
        result.FormattedPreview.ShouldNotBeNull();
        result.FormattedPreview.ShouldContain("\n");
        result.ParseError.ShouldBeNull();
    }

    [Fact]
    public async Task Inspect_ClassifiesJsonArrayAndScalar()
    {
        var runtimeNode = CreateNode(new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<PayloadInspectionResult>(
            runtimeNode,
            PayloadComponentPorts.Output);

        await input.Target.SendAsync(new PayloadInspectionRequest { Text = "[1,2]" });
        await input.Target.SendAsync(new PayloadInspectionRequest { Text = "42" });
        input.Target.Complete();
        var results = await DrainUntilCompletedAsync(output);

        results.Select(result => result.Kind)
            .ShouldBe([PayloadKind.JsonArray, PayloadKind.JsonScalar]);
    }

    [Fact]
    public async Task Inspect_ClassifiesXml()
    {
        var runtimeNode = CreateNode(new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<PayloadInspectionResult>(
            runtimeNode,
            PayloadComponentPorts.Output);

        await input.Target.SendAsync(new PayloadInspectionRequest
        {
            Text = "<root><value>1</value></root>",
            ContentType = "application/xml"
        });
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Kind.ShouldBe(PayloadKind.Xml);
        result.FormattedPreview.ShouldNotBeNull();
        result.FormattedPreview.ShouldContain("<root>");
        result.FormattedPreview.ShouldContain("<value>1</value>");
        result.ParseError.ShouldBeNull();
    }

    [Fact]
    public async Task Inspect_DecodesBytesWithContentTypeCharset()
    {
        var runtimeNode = CreateNode(new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<PayloadInspectionResult>(
            runtimeNode,
            PayloadComponentPorts.Output);
        var bytes = Encoding.Unicode.GetBytes("""{"name":"flux"}""");

        await input.Target.SendAsync(new PayloadInspectionRequest
        {
            Bytes = bytes,
            ContentType = "application/json; charset=utf-16"
        });
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Kind.ShouldBe(PayloadKind.JsonObject);
        result.ByteCount.ShouldBe(bytes.Length);
        result.DetectedEncoding.ShouldBe("utf-16");
        result.TextPreview.ShouldNotBeNull();
        result.TextPreview.ShouldContain("\"name\"");
    }

    [Fact]
    public async Task Inspect_ClassifiesBase64()
    {
        var runtimeNode = CreateNode(new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<PayloadInspectionResult>(
            runtimeNode,
            PayloadComponentPorts.Output);

        await input.Target.SendAsync(new PayloadInspectionRequest
        {
            Text = Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"))
        });
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Kind.ShouldBe(PayloadKind.Base64);
        result.Base64DecodedByteCount.ShouldBe(5);
        result.FormattedPreview.ShouldBe("hello");
        result.FormattedPreviewTruncated.ShouldBeFalse();
    }

    [Fact]
    public async Task Inspect_ClassifiesBinary()
    {
        var runtimeNode = CreateNode(new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<PayloadInspectionResult>(
            runtimeNode,
            PayloadComponentPorts.Output);

        await input.Target.SendAsync(new PayloadInspectionRequest
        {
            Bytes = [0x00, 0x9F, 0xFF]
        });
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Kind.ShouldBe(PayloadKind.Binary);
        result.ByteCount.ShouldBe(3);
        result.TextPreview.ShouldBeNull();
    }

    [Fact]
    public async Task Inspect_ClassifiesEmpty()
    {
        var runtimeNode = CreateNode(new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<PayloadInspectionResult>(
            runtimeNode,
            PayloadComponentPorts.Output);

        await input.Target.SendAsync(new PayloadInspectionRequest());
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Kind.ShouldBe(PayloadKind.Empty);
        result.ByteCount.ShouldBe(0);
        result.TextPreview.ShouldBe(string.Empty);
    }

    [Fact]
    public async Task Inspect_ReportsParseErrorAsResultMetadata()
    {
        var runtimeNode = CreateNode(new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<PayloadInspectionResult>(
            runtimeNode,
            PayloadComponentPorts.Output);

        await input.Target.SendAsync(new PayloadInspectionRequest
        {
            Text = "{",
            ContentType = "application/json"
        });
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.Kind.ShouldBe(PayloadKind.Text);
        result.ParseError.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Inspect_TruncatesPreviews()
    {
        var runtimeNode = CreateNode(new
        {
            maxPreviewBytes = 3,
            maxFormattedChars = 10
        });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<PayloadInspectionResult>(
            runtimeNode,
            PayloadComponentPorts.Output);

        await input.Target.SendAsync(new PayloadInspectionRequest
        {
            Text = """{"message":"abcdef"}""",
            ContentType = "application/json"
        });
        input.Target.Complete();
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        result.TextPreview.ShouldBe("""{"m""");
        result.TextPreviewTruncated.ShouldBeTrue();
        result.FormattedPreview.ShouldNotBeNull();
        result.FormattedPreview!.Length.ShouldBe(10);
        result.FormattedPreviewTruncated.ShouldBeTrue();
    }

    [Fact]
    public async Task Inspect_EmitsErrorsAndContinues()
    {
        var runtimeNode = CreateNode(new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<PayloadInspectionResult>(
            runtimeNode,
            PayloadComponentPorts.Output);
        var errors = LinkOutput<FlowError>(
            runtimeNode,
            PayloadComponentPorts.Errors);

        await input.Target.SendAsync(new PayloadInspectionRequest
        {
            Text = "hello",
            EncodingHint = "missing-encoding"
        });
        await input.Target.SendAsync(new PayloadInspectionRequest
        {
            Text = "hello"
        });
        input.Target.Complete();

        var error = await errors.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var result = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        error.Code.ShouldBe(PayloadErrorCodes.UnsupportedEncoding);
        result.Kind.ShouldBe(PayloadKind.Text);
        result.TextPreview.ShouldBe("hello");
    }

    [Fact]
    public async Task Inspect_EmitsDiagnostics()
    {
        var runtimeNode = CreateNode(new { });
        var input = GetInput(runtimeNode);
        var output = LinkOutput<PayloadInspectionResult>(
            runtimeNode,
            PayloadComponentPorts.Output);
        var diagnostics = new BufferBlock<FlowDiagnostic>();
        runtimeNode.Node.ShouldBeAssignableTo<IFlowDiagnosticSource>()!
            .Diagnostics.LinkTo(
                diagnostics,
                new DataflowLinkOptions { PropagateCompletion = true });

        await input.Target.SendAsync(new PayloadInspectionRequest { Text = "hello" });
        input.Target.Complete();
        await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await runtimeNode.Node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await output.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var diagnostic = await diagnostics.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        diagnostic.Name.ShouldBe(PayloadDiagnosticNames.Inspected);
        diagnostic.Attributes["kind"].ShouldBe("Text");
        diagnostic.Attributes["byteCount"].ShouldBe(5);
    }

    [Fact]
    public void Inspect_RejectsInvalidOptions()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => CreateNode(new { boundedCapacity = 0 }));

        exception.Message.ShouldContain("boundedCapacity");
    }

    [Fact]
    public void RegisterPayloadComponents_RegistersInspectNode()
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterPayloadComponents();

        registry.TryGetFactory(PayloadComponentTypes.Inspect, out _).ShouldBeTrue();
    }

    private static RuntimeNode CreateNode(object configuration)
    {
        var registry = new RuntimeNodeFactoryRegistry()
            .RegisterPayloadComponents();
        registry.TryGetFactory(PayloadComponentTypes.Inspect, out var factory).ShouldBeTrue();
        return factory(CreateContext(configuration));
    }

    private static RuntimeNodeFactoryContext CreateContext(object configuration)
    {
        var root = JsonSerializer.SerializeToElement(configuration);
        var values = root.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());

        return new RuntimeNodeFactoryContext(
            new NodeName("inspect"),
            new NodeDefinition
            {
                Type = PayloadComponentTypes.Inspect,
                Configuration = values
            },
            "main",
            new Dictionary<NodeName, RuntimeNode>());
    }

    private static InputPort<PayloadInspectionRequest> GetInput(RuntimeNode runtimeNode)
        => runtimeNode.FindInput(new PortName(PayloadComponentPorts.Input))
            .ShouldBeOfType<InputPort<PayloadInspectionRequest>>();

    private static BufferBlock<T> LinkOutput<T>(
        RuntimeNode runtimeNode,
        string portName)
    {
        var target = new BufferBlock<T>();
        runtimeNode.FindOutput(new PortName(portName))!
            .TryLinkTo(
                new InputPort<T>(
                    new PortAddress("test", new NodeName("items"), new PortName("Input")),
                    target),
                propagateCompletion: true,
                out var error);
        error.ShouldBeNull();
        return target;
    }

    private static async Task<List<T>> DrainUntilCompletedAsync<T>(
        BufferBlock<T> output)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var values = new List<T>();
        while (await output.OutputAvailableAsync(cancellation.Token))
        {
            while (output.TryReceive(out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }
}
