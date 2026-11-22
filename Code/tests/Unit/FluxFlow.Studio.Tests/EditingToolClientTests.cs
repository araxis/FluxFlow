using System.Text.Json;
using Microsoft.Extensions.AI;
using Shouldly;
using Xunit;
using static FluxFlow.Studio.Tests.ConfigurationEditingTests;

namespace FluxFlow.Studio.Tests;

public sealed class EditingToolClientTests
{
    [Fact]
    public async Task Host_color_tool_uses_standard_tool_calls_and_preserves_completed_arguments()
    {
        var graph = StudioTestServices.CreateGraph();
        graph.LoadJson(Original, "Application");
        var workspace = new StudioConversationWorkspace(graph);
        var before = graph.ToProjectJson();
        var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>("""{"Address":"main.source","Color":"#0000FF"}""")!;
        using var client = new RecordingClient(new(ChatRole.Assistant, [new FunctionCallContent("color", "set_component_color", arguments)]));
        var request = new ProposalRequest("Make it blue", Original, "main", "Application")
        {
            Stepwise = true, Tools = workspace.Tools.ToArray(),
            Steps = [new([], "Applied.", new("set_component_color", JsonSerializer.SerializeToElement(arguments)))]
        };
        var transported = JsonSerializer.Deserialize(JsonSerializer.Serialize(request, ProposalJsonContext.Default.ProposalRequest),
            ProposalJsonContext.Default.ProposalRequest)!;
        var result = await new EditingToolClient(client).GenerateAsync(transported, Original, default);
        result.Continue.ShouldBeTrue();
        result.Edits.ShouldBeEmpty();
        result.Tool!.Name.ShouldBe("set_component_color");
        result.Tool.Arguments.GetProperty("Color").GetString().ShouldBe("#0000FF");
        client.Options!.Tools!.Count.ShouldBe(2);
        var completed = client.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Single();
        completed.Name.ShouldBe("set_component_color");
        JsonSerializer.SerializeToElement(completed.Arguments).GetProperty("Address").GetString().ShouldBe("main.source");
        graph.ToProjectJson().ShouldBe(before);
        var roundTrip = JsonSerializer.Deserialize(JsonSerializer.Serialize(result, ProposalJsonContext.Default.ProposalResult),
            ProposalJsonContext.Default.ProposalResult)!;
        workspace.Prepare(roundTrip.Tool!, "main").Summary.ShouldNotBeNull().ShouldContain("#0000FF");
    }

    [Fact]
    public async Task Streaming_delivers_text_before_the_complete_edit_result()
    {
        using var client = new StreamingClient();
        var text = new List<string>();
        var generating = new EditingToolClient(client).GenerateAsync(new("Add", Original, "main"), Original, default,
            chunk => { text.Add(chunk); return Task.CompletedTask; });
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        text.ShouldBe(["Adding ", "a mapper."]);
        generating.IsCompleted.ShouldBeFalse();
        client.Finish.SetResult();
        var result = await generating;
        text.ShouldBe(["Adding ", "a mapper.", " Ready for review."]);
        result.Edits.Single().ShouldBeOfType<AddComponentEdit>().Address.Value.ShouldBe("main.new");
        result.Continue.ShouldBeTrue();
    }

    [Fact]
    public async Task Cancellation_rejects_late_stream_updates_even_when_the_client_ignores_the_token()
    {
        using var client = new StreamingClient(ignoreCancellation: true);
        using var cancellation = new CancellationTokenSource();
        var text = new List<string>();
        var generating = new EditingToolClient(client).GenerateAsync(new("Add", Original, "main") { Images = [ImageSamples.Png] }, Original, cancellation.Token,
            chunk => { text.Add(chunk); return Task.CompletedTask; });
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        client.Token.IsCancellationRequested.ShouldBeTrue();
        client.Finish.SetResult();
        await Should.ThrowAsync<OperationCanceledException>(() => generating);
        text.ShouldBe(["Adding ", "a mapper."]);
    }

    private sealed class StreamingClient(bool ignoreCancellation = false) : IChatClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            foreach (var image in messages.SelectMany(message => message.Contents).OfType<DataContent>())
                image.Data.ToArray().ShouldBe(Convert.FromBase64String(ImageSamples.Png.Base64));
            yield return new() { Role = ChatRole.Assistant, Contents = [new TextReasoningContent("Internal planning </think>")] };
            yield return new(ChatRole.Assistant, "Adding ");
            yield return new() { Role = ChatRole.Assistant, Contents = [new TextReasoningContent("More planning"), new TextContent("a mapper.")] };
            Started.SetResult();
            if (ignoreCancellation) await Finish.Task;
            else await Finish.Task.WaitAsync(cancellationToken);
            yield return new() { Role = ChatRole.Assistant, Contents = [new TextContent(" Ready for review."), new FunctionCallContent("edit", "edit_workflow",
                JsonSerializer.Deserialize<Dictionary<string, object?>>("""{"Edits":[{"Kind":"add","Address":"main.new","Type":"mapper"}]}""")!)] };
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task Editing_calls_and_completed_results_use_standard_chat_content_without_executing_edits()
    {
        var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>("""
            {"Edits":[{"Address":"main.new","Type":"mapper","Kind":"add"}],"Message":"Add the mapper."}
            """)!;
        using var client = new RecordingClient(new(ChatRole.Assistant, [new FunctionCallContent("next", "edit_workflow", arguments)]));
        var request = new ProposalRequest("Add a mapper", Original, "main", "Application")
        { Stepwise = true, Images = [ImageSamples.Png, ImageSamples.Jpeg], Steps = [new([new AddComponentEdit(Address("main.first"), "source")], "Applied.")] };
        var generated = await new EditingToolClient(client).GenerateAsync(request, Original, default);
        generated.Continue.ShouldBeTrue();
        generated.Edits.Single().ShouldBeOfType<AddComponentEdit>().Address.Value.ShouldBe("main.new");
        client.Options!.AllowMultipleToolCalls.ShouldBe(false);
        client.Options.Tools!.Single().ShouldBeAssignableTo<AIFunctionDeclaration>();
        client.Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Single().Result.ShouldBe("Applied.");
        client.Messages.Last().Text.ShouldContain(Original);
        var images = client.Messages.Last().Contents.OfType<DataContent>().ToArray();
        images.Length.ShouldBe(2);
        images[0].MediaType.ShouldBe("image/png");
        images[0].Data.ToArray().ShouldBe(Convert.FromBase64String(ImageSamples.Png.Base64));
        images[1].MediaType.ShouldBe("image/jpeg");
        images[1].Data.ToArray().ShouldBe(Convert.FromBase64String(ImageSamples.Jpeg.Base64));
        client.Messages.Last().Text.ShouldNotContain(ImageSamples.Png.Base64);
        request.Configuration.ShouldBe(Original);
    }

    [Fact]
    public async Task Text_answers_return_without_editing_tools()
    {
        const string answer = "The workflow contains a mapper. Literal text: `</think>`.";
        using var client = new RecordingClient(new(ChatRole.Assistant,
            [new TextReasoningContent("Private planning"), new TextContent(answer)]));
        var result = await new EditingToolClient(client).GenerateAsync(new("Explain", Original, "main"), Original, default);
        result.Edits.ShouldBeEmpty();
        result.Continue.ShouldBeFalse();
        result.Message.ShouldBe(answer);
    }

    [Fact]
    public async Task Reasoning_without_an_answer_or_tools_is_not_displayed_as_a_reply()
    {
        using var client = new RecordingClient(new(ChatRole.Assistant, [new TextReasoningContent("Private planning")]));
        var error = await Should.ThrowAsync<InvalidOperationException>(() =>
            new EditingToolClient(client).GenerateAsync(new("Explain", Original, "main"), Original, default));
        error.Message.ShouldContain("no answer or editing tools");
    }

    [Fact]
    public async Task Unknown_tools_are_rejected()
    {
        using var client = new RecordingClient(new(ChatRole.Assistant, [new FunctionCallContent("bad", "run_workflow", new Dictionary<string, object?>())]));
        await Should.ThrowAsync<InvalidOperationException>(() => new EditingToolClient(client).GenerateAsync(new("Run", Original, "main"), Original, default));
    }

    private sealed class RecordingClient(ChatMessage response) : IChatClient
    {
        public List<ChatMessage> Messages { get; private set; } = [];
        public ChatOptions? Options { get; private set; }
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        { Messages = messages.ToList(); Options = options; return Task.FromResult(new ChatResponse(response)); }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
