using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluxFlow.Authoring.Definitions;
using FluxFlow.Conversations;
using FluxFlow.Studio.UI.Features.Designer;
using FluxFlow.Studio.UI.Features.Designer.Canvas;
using FluxFlow.StudioWebHost;
using FluxFlow.Conversations.Local;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluxFlow.Studio.Tests.ConfigurationEditingTests;

namespace FluxFlow.Studio.Tests;

public sealed class ProposalGenerationTests
{
    [Fact]
    public async Task Maximum_image_attachments_fit_the_request_limit_without_changing_contents()
    {
        var data = new byte[ImageAttachment.MaxFileBytes];
        // Repeating these bytes produces '+' in base64, which the default JSON encoder expands.
        byte[] pattern = [251, 239, 190];
        for (var index = 0; index < data.Length; index++) data[index] = pattern[index % pattern.Length];
        Convert.FromBase64String(ImageSamples.Png.Base64).CopyTo(data, 0);
        var image = ImageSamples.Png with { Base64 = Convert.ToBase64String(data) };
        var request = new ProposalRequest("Explain <sample> & preserve casing", Original, "main", "Application")
        {
            Images = [image, image with { Name = "second.png" }],
            Attachments = [new("sample.json", "{\"Reading\":1,\"reading\":2,\"Label\":\"<sample> + café\"}")]
        };
        ImageAttachment.Validate(request.Images);
        JsonSerializer.SerializeToUtf8Bytes(request, ProposalJsonContext.Default.ProposalRequest).Length.ShouldBeGreaterThan(8 * 1024 * 1024);
        using var content = JsonContent.Create(request, ProposalJsonContext.Transport.ProposalRequest);
        var body = await content.ReadAsByteArrayAsync();
        body.Length.ShouldBeLessThan(8 * 1024 * 1024);
        var restored = JsonSerializer.Deserialize(body, ProposalJsonContext.Default.ProposalRequest)!;
        restored.Images.ShouldBe(request.Images);
        restored.Instructions.ShouldBe(request.Instructions);
        restored.Configuration.ShouldBe(request.Configuration);
        restored.Attachments.ShouldBe(request.Attachments);
    }

    [Fact]
    public async Task Host_flushes_partial_text_before_the_complete_result()
    {
        var generator = new StreamingGenerator();
        await using var app = StudioHost.Create([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IProposalGenerator>(generator);
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/proposals/stream")
        { Content = JsonContent.Create(new ProposalRequest("Explain", Original, "main") { Stepwise = true, SelectedComponents = ["main.source", "main.second"], SelectedWorkflows = ["other", "main"], Attachments = [new("sample.json", "{\"Reading\":1}")], Images = [ImageSamples.Png, ImageSamples.Jpeg] }, ProposalJsonContext.Transport.ProposalRequest) };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        var first = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        JsonSerializer.Deserialize(first!, ProposalJsonContext.Default.ProposalUpdate)!.Text.ShouldBe("Hello\nworkflow");
        generator.Finish.Task.IsCompleted.ShouldBeFalse();
        generator.Request!.SelectedComponents.ShouldBe(new[] { "main.source", "main.second" });
        generator.Request.SelectedWorkflows.ShouldBe(new[] { "other", "main" });
        generator.Request.Attachments.ShouldBe(new[] { new TextAttachment("sample.json", "{\"Reading\":1}") });
        generator.Request.Images.ShouldBe(new[] { ImageSamples.Png, ImageSamples.Jpeg });
        generator.Finish.SetResult();
        var last = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        JsonSerializer.Deserialize(last!, ProposalJsonContext.Default.ProposalUpdate)!.Result!.Message.ShouldBe("Complete");
    }

    private sealed class StreamingGenerator : IProposalGenerator
    {
        public ProposalRequest? Request { get; private set; }
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ProposalResult> GenerateAsync(ProposalRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<ProposalResult> GenerateAsync(ProposalRequest request, Func<string, Task> onText, CancellationToken cancellationToken = default)
        {
            Request = request;
            await onText("Hello\nworkflow");
            await Finish.Task.WaitAsync(cancellationToken);
            return new() { Message = "Complete" };
        }
    }

    [Theory]
    [InlineData("record")]
    [InlineData("undo")]
    [InlineData("direct")]
    [InlineData("replace")]
    [InlineData("unfinished")]
    public async Task Edits_during_generation_reject_the_late_response(string change)
    {
        var graph = CreateGraph();
        var delayed = new DelayedGenerator();
        using var preview = new ConversationPreview(new StudioConversationWorkspace(graph), delayed);
        var connection = new ProposalConnection { Model = "selected" };
        var generating = preview.GenerateAsync("Add a source and mapper.", connection: connection);
        preview.IsGenerating.ShouldBeTrue();
        delayed.Request!.Configuration.ShouldBe(graph.ToJson());
        delayed.Request.SectionPath.ShouldBe("Application");
        delayed.Request.Connection.ShouldBe(connection);
        IDisposable? editing = null;
        if (change == "replace") graph.LoadJson("""{"Workflows":{"main":{}}}""");
        else if (change == "unfinished") editing = graph.BeginManualEdit();
        else
        {
            graph.Diagram.Nodes.OfType<FlowNodeModel>().Single().Configuration["expression"] = Json("\"manual\"");
            if (change != "direct") graph.RecordEdit();
            if (change == "undo") graph.Undo();
        }
        var before = graph.ToProjectJson();
        delayed.Complete();
        await Should.ThrowAsync<InvalidOperationException>(() => generating);
        editing?.Dispose();
        preview.IsGenerating.ShouldBeFalse();
        preview.Proposal.ShouldBeNull();
        graph.ToProjectJson().ShouldBe(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discard_cancels_generation_and_ignores_late_completion_without_disturbing_a_new_request(bool fail)
    {
        var graph = CreateGraph();
        var delayed = new DelayedGenerator();
        using var preview = new ConversationPreview(new StudioConversationWorkspace(graph), delayed);
        var before = graph.ToProjectJson();
        var first = preview.GenerateAsync("First request");
        var firstCompletion = delayed.Completion;
        var token = delayed.Token;
        await Should.ThrowAsync<InvalidOperationException>(() => preview.GenerateAsync("Duplicate request"));
        preview.Discard();
        token.IsCancellationRequested.ShouldBeTrue();
        var second = preview.GenerateAsync("Second request");
        if (fail) firstCompletion.SetException(new HttpRequestException("Late connection failure"));
        else firstCompletion.SetResult(FixedProposalGenerator.Result());
        await Should.ThrowAsync<OperationCanceledException>(() => first);
        preview.IsGenerating.ShouldBeTrue();
        preview.Proposal.ShouldBeNull();
        delayed.Complete();
        await second;
        preview.Proposal.ShouldNotBeNull();
        graph.ToProjectJson().ShouldBe(before);
    }

    [Fact]
    public async Task Navigation_during_generation_keeps_the_original_target()
    {
        var graph = CreateGraph();
        var delayed = new DelayedGenerator();
        using var preview = new ConversationPreview(new StudioConversationWorkspace(graph), delayed);
        var generating = preview.GenerateAsync("Add a source.");
        graph.SelectWorkflow("other");
        delayed.Complete();
        await generating;
        preview.Workflow.ShouldBe("main");
        preview.Apply().ShouldBeTrue();
        graph.WorkflowName.ShouldBe("other");
    }

    [Fact]
    public async Task Host_uses_DI_descriptions_and_native_edits_without_mutating_the_document()
    {
        using var services = new ServiceCollection().AddCatalogComponents(StudioTestServices.ProcessingCatalog).BuildServiceProvider();
        using var handler = new ReplyHandler("""
            {"Edits":[
              {"Kind":"add","Address":"main.new-map","Type":"mapper","Properties":{}},
              {"Kind":"configure","Address":"main.new-map","Properties":{"Expression":"Reading * 2"}},
              {"Kind":"connect","Source":"main.source.Output","Target":"main.new-map.Input"}
            ],"Error":null}
            """);
        using var client = new HttpClient(handler);
        var generator = new LocalProposalGenerator(new TestClientFactory(handler, client.Timeout), Settings(), services.GetServices<IComponentDescription>());
        var request = new ProposalRequest("Double each Reading", Original, "main", "Application", new() { Model = "selected" })
        { SelectedComponents = ["main.source", "main.second", "main.source"] };
        request = request with { Configuration = new DesignerConfiguration(Original, "Application")
            .PrepareEdits([new AddComponentEdit(Address("main.second"), "batch", new Dictionary<string, JsonElement> { ["BatchSize"] = Json("3") })]).ToJson() };
        var configuration = request.Configuration;
        var result = await generator.GenerateAsync(request);
        result.Error.ShouldBeNull();
        result.Edits.Length.ShouldBe(3);
        ((AddComponentEdit)result.Edits[0]).Properties!["Engine"].GetString().ShouldBe("jsonata");
        request.Configuration.ShouldBe(configuration);
        var sent = JsonDocument.Parse(handler.Body!);
        sent.RootElement.GetProperty("stream").GetBoolean().ShouldBeFalse();
        sent.RootElement.GetProperty("model").GetString().ShouldBe("selected");
        var context = JsonDocument.Parse(sent.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
        context.RootElement.GetProperty("AvailableComponents").EnumerateArray()
            .Select(item => item.GetProperty("Type").GetString()).Order(StringComparer.Ordinal)
            .ShouldBe(services.GetServices<IComponentDescription>().Select(item => item.Palette.ComponentType).Order(StringComparer.Ordinal));
        context.RootElement.GetProperty("ExistingComponents")[0].GetProperty("Address").GetString().ShouldBe("main.source");
        var attached = context.RootElement.GetProperty("SelectedComponents")[0];
        context.RootElement.GetProperty("SelectedComponents").GetArrayLength().ShouldBe(2);
        context.RootElement.GetProperty("SelectedComponents")[1].GetProperty("Address").GetString().ShouldBe("main.second");
        context.RootElement.GetProperty("SelectedComponents")[1].GetProperty("Properties").GetProperty("BatchSize").GetInt32().ShouldBe(3);
        attached.GetProperty("Address").GetString().ShouldBe("main.source");
        attached.GetProperty("Type").GetString().ShouldBe("mapper");
        attached.GetProperty("Properties").GetProperty("expression").GetString().ShouldBe("$");
        attached.GetProperty("Properties").GetProperty("Custom").GetProperty("UPPER").GetInt32().ShouldBe(42);
        context.RootElement.GetProperty("Configuration").GetProperty("Host").GetProperty("KeepCase").GetBoolean().ShouldBeTrue();
        var candidate = new DesignerConfiguration(Original, "Application").PrepareEdits(result.Edits);
        candidate.Nodes("main").Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"Edits\":[{\"Kind\":\"delete\",\"Address\":\"main.source\"}]}")]
    [InlineData("{\"Edits\":[{\"Kind\":\"add\",\"Address\":null,\"Type\":\"mapper\"}]}")]
    [InlineData("{\"Edits\":[{\"Kind\":\"add\",\"Type\":\"mapper\"}]}")]
    [InlineData("{\"Edits\":[{\"Kind\":\"add\",\"Address\":\"main.a\",\"Type\":\"unregistered\"}]}")]
    [InlineData("{\"Edits\":[{\"Kind\":\"add\",\"Address\":\"main.a\",\"Type\":\"mapper\",\"Properties\":{\"Engine\":\"jsonata\",\"engine\":\"other\"}}]}")]
    [InlineData("{\"Edits\":[]}")]
    public async Task Invalid_provider_output_never_becomes_an_edit_batch(string text)
    {
        using var services = new ServiceCollection().AddCatalogComponents(StudioTestServices.ProcessingCatalog).BuildServiceProvider();
        using var handler = new ReplyHandler(text);
        using var client = new HttpClient(handler);
        var result = await new LocalProposalGenerator(new TestClientFactory(handler, client.Timeout), Settings(), services.GetServices<IComponentDescription>())
            .GenerateAsync(new("Change the design", Original, "main", "Application"));
        result.Error.ShouldNotBeNullOrWhiteSpace();
        result.Edits.ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_settings_remote_endpoint_and_service_failure_are_reported_without_edits()
    {
        using var handler = new ReplyHandler("{}") { Status = HttpStatusCode.ServiceUnavailable };
        using var client = new HttpClient(handler);
        var settings = Settings();
        settings["Proposals:Model"] = null;
        var generator = new LocalProposalGenerator(new TestClientFactory(handler, client.Timeout), settings, []);
        var request = new ProposalRequest("Add a source", "{\"Workflows\":{\"main\":{}}}", "main");
        (await generator.GenerateAsync(request)).Error!.ShouldContain("Proposals:Model");
        handler.Body.ShouldBeNull();
        settings["Proposals:Model"] = "test";
        settings["Proposals:Endpoint"] = "https://example.com";
        (await generator.GenerateAsync(request)).Error!.ShouldContain("local HTTP");
        handler.Body.ShouldBeNull();
        settings["Proposals:Endpoint"] = null;
        (await generator.GenerateAsync(request)).Error!.ShouldContain("503");
    }

    [Fact]
    public async Task Web_endpoint_transports_typed_edits_and_preserves_property_casing()
    {
        await using var app = StudioHost.Create([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IProposalGenerator, FixedProposalGenerator>();
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync("/api/proposals/generate", new ProposalRequest("Add components", Original, "main", "Application")
            { Stepwise = true, Steps = [new(FixedProposalGenerator.Result().Edits, "Applied.")] },
            ProposalJsonContext.Default.ProposalRequest);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync(ProposalJsonContext.Default.ProposalResult);
        result!.Edits.Length.ShouldBe(4);
        ((AddComponentEdit)result.Edits[0]).Address.Value.ShouldBe("main.sample-source");
        ((AddComponentEdit)result.Edits[0]).Properties!.ContainsKey("Values").ShouldBeTrue();
        await app.StopAsync();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Cancellation_reaches_the_service_and_timeout_returns_no_edits(bool cancel, bool stepwise)
    {
        using var handler = new WaitingHandler();
        using var client = new HttpClient(handler) { Timeout = cancel ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(100) };
        using var cancellation = new CancellationTokenSource();
        var generator = new LocalProposalGenerator(new TestClientFactory(handler, client.Timeout), Settings(), []);
        var pending = generator.GenerateAsync(new("Add a source", "{\"Workflows\":{\"main\":{}}}", "main") { Stepwise = stepwise }, cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (cancel)
        {
            cancellation.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => pending);
        }
        else
        {
            var result = await pending;
            result.Error!.ShouldContain("timed out");
            result.Edits.ShouldBeEmpty();
        }
        handler.Token.IsCancellationRequested.ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "test does not support tools")]
    [InlineData(HttpStatusCode.OK, "generation interrupted")]
    public async Task Native_service_errors_are_reported_without_edits(HttpStatusCode status, string error)
    {
        using var handler = new ReplyHandler("") { Status = status, Error = error };
        var generator = new LocalProposalGenerator(new TestClientFactory(handler, TimeSpan.FromSeconds(5)), Settings(), []);
        var request = new ProposalRequest("Add a component", "{\"Workflows\":{\"main\":{}}}", "main") { Stepwise = true };
        var result = await generator.GenerateAsync(request);
        if (status == HttpStatusCode.BadRequest) result.Error.ShouldBe(error);
        else result.Error!.ShouldContain("incomplete");
        result.Edits.ShouldBeEmpty();
    }

    private static DesignerGraphState CreateGraph()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Original, "Application");
        graph.ResetHistory();
        return graph;
    }

    [Theory]
    [InlineData("main.pulse", false)]
    [InlineData("other.pulse", true)]
    public async Task Native_tool_transport_preserves_properties_and_rejects_another_workflow(string address, bool rejected)
    {
        using var services = new ServiceCollection().AddCatalogComponents(StudioTestServices.ProcessingCatalog).BuildServiceProvider();
        using var handler = new NativeReplyHandler(address);
        var generator = new LocalProposalGenerator(new TestClientFactory(handler, TimeSpan.FromSeconds(5)), Settings(), services.GetServices<IComponentDescription>());
        TextAttachment[] files = [new("payload.json", " {\"Reading\": 12, \"reading\": 3} \n"), new("notes.txt", "Line 1\r\nLine 2: café")];
        var result = await generator.GenerateAsync(new("Add a Scheduler", Original, "main", "Application") { Stepwise = true, Attachments = files });
        if (rejected)
        {
            result.Error!.ShouldContain("active workflow");
            result.Edits.ShouldBeEmpty();
        }
        else
        {
            result.Error.ShouldBeNull();
            result.Continue.ShouldBeTrue();
            result.Edits.Single().ShouldBeOfType<AddComponentEdit>().Properties!["Interval"].GetString().ShouldBe("00:00:05");
        }
        using var sent = JsonDocument.Parse(handler.Body!);
        sent.RootElement.TryGetProperty("think", out _).ShouldBeFalse();
        sent.RootElement.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString().ShouldBe("edit_workflow");
        var contextText = sent.RootElement.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString()!;
        using var context = JsonDocument.Parse(contextText[(contextText.IndexOf('\n') + 1)..]);
        var attachments = context.RootElement.GetProperty("Attachments");
        attachments.GetArrayLength().ShouldBe(2);
        for (var index = 0; index < files.Length; index++)
        {
            attachments[index].GetProperty("Name").GetString().ShouldBe(files[index].Name);
            attachments[index].GetProperty("Content").GetString().ShouldBe(files[index].Content);
        }
        // A later request may select another local endpoint without reusing a started HttpClient.
        await generator.GenerateAsync(new("Add a Scheduler", Original, "main", "Application", new() { Model = "test", Endpoint = "http://localhost:11435/" }) { Stepwise = true });
        handler.Endpoint!.Port.ShouldBe(11435);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_response_keeps_reasoning_separate_from_answer_text(bool stream)
    {
        using var services = new ServiceCollection().AddCatalogComponents(StudioTestServices.ProcessingCatalog).BuildServiceProvider();
        using var handler = new ReasoningReplyHandler();
        var generator = new LocalProposalGenerator(new TestClientFactory(handler, TimeSpan.FromSeconds(5)), Settings(), services.GetServices<IComponentDescription>());
        var request = new ProposalRequest("Explain", Original, "main", "Application") { Stepwise = true };
        var chunks = new List<string>();
        var result = stream
            ? await generator.GenerateAsync(request, text => { chunks.Add(text); return Task.CompletedTask; })
            : await generator.GenerateAsync(request);
        result.Error.ShouldBeNull();
        result.Edits.ShouldBeEmpty();
        result.Message.ShouldBe("Reading is 12.");
        if (stream) chunks.ShouldBe(new[] { "Reading ", "is 12." });
        else chunks.ShouldBeEmpty();
        request.Configuration.ShouldBe(Original);
    }

    private sealed class ReasoningReplyHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var sent = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            sent.RootElement.TryGetProperty("think", out _).ShouldBeFalse();
            var streaming = sent.RootElement.GetProperty("stream").GetBoolean();
            var reply = streaming ? """
                {"model":"test","message":{"role":"assistant","thinking":"Internal planning </think>","content":""},"done":false}
                {"model":"test","message":{"role":"assistant","thinking":"More planning","content":"Reading "},"done":false}
                {"model":"test","message":{"role":"assistant","content":"is 12."},"done":false}
                {"model":"test","message":{"role":"assistant","content":""},"done":true,"done_reason":"stop"}
                """ : """
                {"model":"test","message":{"role":"assistant","thinking":"Internal planning </think>","content":"Reading is 12."},"done":true,"done_reason":"stop"}
                """;
            return new(HttpStatusCode.OK) { Content = new StringContent(reply, System.Text.Encoding.UTF8, streaming ? "application/x-ndjson" : "application/json") };
        }
    }

    private sealed class NativeReplyHandler(string address) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public Uri? Endpoint { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Endpoint = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var reply = """
                {"model":"test","done":true,"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"edit_workflow","arguments":{"Edits":[{"Address":"ADDRESS","Properties":{"Interval":"00:00:05"},"Type":"scheduler","Kind":"add"}],"Message":"Add a Scheduler."}}}]}}
                """.Replace("ADDRESS", address);
            return new(HttpStatusCode.OK) { Content = new StringContent(reply, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    [Theory]
    [InlineData("vision", false)]
    [InlineData("vision", true)]
    [InlineData("completion", true)]
    [InlineData(null, true)]
    public async Task Images_require_reported_capability_and_use_native_image_content(string? capability, bool stepwise)
    {
        using var services = new ServiceCollection().AddCatalogComponents(StudioTestServices.ProcessingCatalog).BuildServiceProvider();
        using var handler = new ImageReplyHandler(capability);
        var generator = new LocalProposalGenerator(new TestClientFactory(handler, TimeSpan.FromSeconds(5)), Settings(), services.GetServices<IComponentDescription>());
        var request = new ProposalRequest("Use the pictures", Original, "main", "Application")
            { Stepwise = stepwise, Images = [ImageSamples.Png, ImageSamples.Jpeg] };
        var result = await generator.GenerateAsync(request);
        handler.Paths[0].ShouldBe("/api/show");
        if (capability != "vision")
        {
            result.Error!.ShouldContain("does not report image support");
            handler.Paths.ShouldBe(new[] { "/api/show" });
            result.Edits.ShouldBeEmpty();
            return;
        }
        result.Error.ShouldBeNull();
        handler.Paths.ShouldBe(new[] { "/api/show", "/api/chat" });
        using var sent = JsonDocument.Parse(handler.Body!);
        var context = sent.RootElement.GetProperty("messages").EnumerateArray().Last();
        context.GetProperty("images").EnumerateArray().Select(item => item.GetString()).ShouldBe(new[] { ImageSamples.Png.Base64, ImageSamples.Jpeg.Base64 });
        context.GetProperty("content").GetString()!.ShouldNotContain(ImageSamples.Png.Base64);
        request.Configuration.ShouldBe(Original);
    }

    [Theory]
    [InlineData("/api/show")]
    [InlineData("/api/chat")]
    public async Task Cancelling_an_image_request_rejects_late_capability_or_generation_responses(string waitAt)
    {
        using var handler = new ImageReplyHandler("vision", waitAt);
        using var cancellation = new CancellationTokenSource();
        var generator = new LocalProposalGenerator(new TestClientFactory(handler, TimeSpan.FromSeconds(5)), Settings(), []);
        var generating = generator.GenerateAsync(new("Explain", Original, "main", "Application")
            { Stepwise = true, Images = [ImageSamples.Png] }, cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        handler.Token.IsCancellationRequested.ShouldBeTrue();
        handler.Finish.SetResult();
        await Should.ThrowAsync<OperationCanceledException>(() => generating);
        if (waitAt == "/api/show") handler.Paths.ShouldBe(new[] { "/api/show" });
    }

    private sealed class ImageReplyHandler(string? capability, string? waitAt = null) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        public string? Body { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var sent = JsonDocument.Parse(Body);
            sent.RootElement.GetProperty("model").GetString().ShouldBe("test");
            if (path == waitAt)
            {
                Token = cancellationToken;
                Started.SetResult();
                await Finish.Task; // Deliberately complete after cancellation.
            }
            if (path == "/api/show")
                return new(HttpStatusCode.OK) { Content = capability is null ? JsonContent.Create(new { }) : JsonContent.Create(new { capabilities = new[] { capability } }) };
            path.ShouldBe("/api/chat");
            var answer = sent.RootElement.TryGetProperty("tools", out _) ? "The image is attached." :
                """{"Edits":[{"Kind":"add","Address":"main.picture","Type":"mapper"}],"Error":null}""";
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { model = "test", done = true, message = new { role = "assistant", content = answer } }) };
        }
    }

    [Theory]
    [InlineData("count")]
    [InlineData("type")]
    [InlineData("signature")]
    [InlineData("base64")]
    [InlineData("size")]
    [InlineData("path")]
    public async Task Invalid_images_are_rejected_before_generation_or_changes(string invalid)
    {
        var image = invalid switch
        {
            "type" => ImageSamples.Png with { MediaType = "image/svg+xml" },
            "signature" => ImageSamples.Png with { Base64 = ImageSamples.Jpeg.Base64 },
            "base64" => ImageSamples.Png with { Base64 = "not base64" },
            "size" => ImageSamples.Png with { Base64 = Convert.ToBase64String(new byte[ImageAttachment.MaxFileBytes + 1]) },
            "path" => ImageSamples.Png with { Name = "folder/sample.png" },
            _ => ImageSamples.Png
        };
        ImageAttachment[] images = invalid == "count" ? [image, image, image] : [image];
        using var handler = new ImageReplyHandler("vision");
        var generator = new LocalProposalGenerator(new TestClientFactory(handler, TimeSpan.FromSeconds(5)), Settings(), []);
        var result = await generator.GenerateAsync(new("Explain", Original, "main", "Application") { Images = images });
        result.Error.ShouldNotBeNullOrWhiteSpace();
        handler.Paths.ShouldBeEmpty();
        var graph = CreateGraph();
        var before = graph.ToProjectJson();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        await Should.ThrowAsync<ArgumentException>(() => chat.SendAsync("Explain", null, images: images));
        chat.Turns.ShouldBeEmpty();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
    }

    private static IConfigurationRoot Settings() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Proposals:Model"] = "test" }).Build();

    private sealed class DelayedGenerator : IProposalGenerator
    {
        public TaskCompletionSource<ProposalResult> Completion { get; private set; } = new();
        public ProposalRequest? Request { get; private set; }
        public CancellationToken Token { get; private set; }
        public Task<ProposalResult> GenerateAsync(ProposalRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            Token = cancellationToken;
            Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return Completion.Task;
        }
        public void Complete() => Completion.SetResult(FixedProposalGenerator.Result());
    }

    [Theory]
    [InlineData("main.missing")]
    [InlineData("other.sink")]
    public async Task Provider_rejects_an_unresolvable_attachment_without_sending_it(string component)
    {
        using var handler = new ReplyHandler("{}");
        var generator = new LocalProposalGenerator(new TestClientFactory(handler, TimeSpan.FromSeconds(5)), Settings(), []);
        var result = await generator.GenerateAsync(new("Explain", Original, "main", "Application") { SelectedComponents = ["main.source", component] });
        result.Error!.ShouldContain("attached component");
        handler.Body.ShouldBeNull();
        result.Edits.ShouldBeEmpty();
    }

    [Fact]
    public async Task Workflow_context_uses_inspection_preserving_unknown_components_and_cross_workflow_links()
    {
        using var services = new ServiceCollection().AddCatalogComponents(StudioTestServices.ProcessingCatalog).BuildServiceProvider();
        using var handler = new ReplyHandler("""
            {"Edits":[{"Kind":"configure","Address":"main.source","Properties":{"Expression":"Reading"}}]}
            """);
        var configuration = new DesignerConfiguration(Original, "Application").PrepareEdits(Batch()).ToJson();
        var generator = new LocalProposalGenerator(new TestClientFactory(handler, TimeSpan.FromSeconds(5)), Settings(), services.GetServices<IComponentDescription>());
        var request = new ProposalRequest("Compare", configuration, "main", "Application") { SelectedWorkflows = ["other", "main", "other"] };
        var result = await generator.GenerateAsync(request);
        result.Error.ShouldBeNull();
        using var body = JsonDocument.Parse(handler.Body!);
        using var context = JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
        context.RootElement.GetProperty("Workflow").GetString().ShouldBe("main");
        var workflows = context.RootElement.GetProperty("SelectedWorkflows");
        workflows.GetArrayLength().ShouldBe(2);
        workflows[0].GetProperty("Name").GetString().ShouldBe("other");
        var unknown = workflows[0].GetProperty("Components").EnumerateArray().Single(item => item.GetProperty("Address").GetString() == "other.unknown");
        unknown.GetProperty("Properties").GetProperty("Extra").GetBoolean().ShouldBeTrue();
        workflows[0].GetProperty("Diagnostics").GetArrayLength().ShouldBeGreaterThan(0);
        var link = workflows[0].GetProperty("Links").EnumerateArray().Single();
        link.GetProperty("Source").GetString().ShouldBe("main.map.Output");
        link.GetProperty("Target").GetString().ShouldBe("other.sink.Input");
        link.GetProperty("Condition").GetString().ShouldBe("$IsError");
        link.GetProperty("IsError").GetBoolean().ShouldBeTrue();
        request.Configuration.ShouldBe(configuration);
    }

    [Fact]
    public async Task Provider_rejects_missing_workflow_context_before_sending()
    {
        using var handler = new ReplyHandler("{}");
        var generator = new LocalProposalGenerator(new TestClientFactory(handler, TimeSpan.FromSeconds(5)), Settings(), []);
        var result = await generator.GenerateAsync(new("Explain", Original, "main", "Application") { SelectedWorkflows = ["main", "missing"] });
        result.Error!.ShouldContain("attached workflow");
        handler.Body.ShouldBeNull();
    }

    [Theory]
    [InlineData("extension")]
    [InlineData("path")]
    [InlineData("binary")]
    [InlineData("size")]
    [InlineData("total")]
    [InlineData("count")]
    [InlineData("null")]
    public async Task Invalid_files_are_rejected_before_generation_or_edits(string kind)
    {
        TextAttachment[] files = kind switch
        {
            "extension" => [new("image.png", "data")],
            "path" => [new("C:\\private\\notes.txt", "data")],
            "binary" => [new("notes.txt", "a\0b")],
            "size" => [new("notes.txt", new string('é', TextAttachment.MaxFileBytes / 2 + 1))],
            "total" => Enumerable.Range(0, 3).Select(index => new TextAttachment($"sample{index}.txt", new string('a', TextAttachment.MaxFileBytes))).ToArray(),
            "count" => Enumerable.Range(0, 5).Select(index => new TextAttachment($"sample{index}.txt", "a")).ToArray(),
            _ => [null!]
        };
        using var handler = new ReplyHandler("{}");
        var generator = new LocalProposalGenerator(new TestClientFactory(handler, TimeSpan.FromSeconds(5)), Settings(), []);
        var result = await generator.GenerateAsync(new("Explain", Original, "main", "Application") { Attachments = files });
        result.Error.ShouldNotBeNullOrWhiteSpace();
        handler.Body.ShouldBeNull();
        var graph = CreateGraph();
        var before = graph.ToProjectJson();
        using var chat = new ConversationSession(new StudioConversationWorkspace(graph), generator);
        await Should.ThrowAsync<ArgumentException>(() => chat.SendAsync("Explain", null, attachments: files));
        chat.Turns.ShouldBeEmpty();
        graph.ToProjectJson().ShouldBe(before);
        graph.CanUndo.ShouldBeFalse();
    }

    private sealed class ReplyHandler(string content) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? Error { get; init; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status)
            {
                Content = Error is null ? JsonContent.Create(new { message = new { content } }) : JsonContent.Create(new { error = Error })
            };
        }
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation was expected.");
        }
    }

    private sealed class TestClientFactory(HttpMessageHandler handler, TimeSpan timeout) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { Timeout = timeout };
    }
}
