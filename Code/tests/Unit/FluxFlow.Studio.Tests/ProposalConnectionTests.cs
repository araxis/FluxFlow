using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluxFlow.Conversations;
using FluxFlow.StudioWebHost;
using FluxFlow.Conversations.Local;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class ProposalConnectionTests
{
    [Fact]
    public async Task Discovery_inherits_host_defaults_and_testing_uses_the_selected_connection()
    {
        using var handler = new ProviderHandler();
        using var client = new HttpClient(handler);
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Proposals:Model"] = "beta", ["Proposals:Endpoint"] = "http://localhost:11434/" }).Build();
        var service = new LocalProposalConnectionService(client, settings);
        var listed = await service.GetModelsAsync(null);
        listed.Error.ShouldBeNull();
        listed.Connection.Model.ShouldBe("beta");
        listed.Models.ShouldBe(["alpha", "beta"]);
        handler.ChatBody.ShouldBeNull();

        var selected = listed.Connection with { Endpoint = "http://127.0.0.1:11435/", Model = "alpha" };
        var tested = await service.TestAsync(selected);
        tested.Error.ShouldBeNull();
        tested.Connection.ShouldBe(selected);
        handler.LastUri!.Port.ShouldBe(11435);
        using var body = JsonDocument.Parse(handler.ChatBody!);
        body.RootElement.GetProperty("model").GetString().ShouldBe("alpha");
        settings["Proposals:Model"].ShouldBe("beta");
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing")]
    public async Task Unselected_or_missing_model_never_starts_generation(string model)
    {
        using var handler = new ProviderHandler();
        using var client = new HttpClient(handler);
        var result = await new LocalProposalConnectionService(client, new ConfigurationBuilder().Build())
            .TestAsync(new() { Model = model });
        result.Error.ShouldNotBeNullOrWhiteSpace();
        handler.ChatBody.ShouldBeNull();
    }

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("http://user@localhost:11434/")]
    [InlineData("http://localhost:11434/other")]
    public async Task Invalid_endpoint_is_rejected_before_network_access(string endpoint)
    {
        using var handler = new ProviderHandler();
        using var client = new HttpClient(handler);
        var result = await new LocalProposalConnectionService(client, new ConfigurationBuilder().Build())
            .GetModelsAsync(new() { Endpoint = endpoint });
        result.Error.ShouldNotBeNull();
        result.Error.ShouldContain("local HTTP");
        handler.LastUri.ShouldBeNull();
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("malformed")]
    [InlineData("empty")]
    [InlineData("timeout")]
    public async Task Service_failures_are_reported_without_changing_the_selected_connection(string failure)
    {
        using var handler = new ProviderHandler { Failure = failure };
        using var client = new HttpClient(handler);
        var selected = new ProposalConnection { Model = "alpha" };
        var result = await new LocalProposalConnectionService(client, new ConfigurationBuilder().Build()).TestAsync(selected);
        result.Error.ShouldNotBeNullOrWhiteSpace();
        result.Connection.ShouldBe(selected);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_to_the_provider()
    {
        using var handler = new ProviderHandler { Failure = "wait" };
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var testing = new LocalProposalConnectionService(client, new ConfigurationBuilder().Build())
            .TestAsync(new() { Model = "alpha" }, cancellation.Token);
        await handler.Started.Task;
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => testing);
        handler.Token.IsCancellationRequested.ShouldBeTrue();
    }

    [Theory]
    [InlineData("models")]
    [InlineData("test")]
    public async Task Web_boundary_preserves_connection_and_model_names(string action)
    {
        using var handler = new ProviderHandler();
        using var providerClient = new HttpClient(handler);
        await using var app = StudioHost.Create([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<IProposalConnectionService>(new LocalProposalConnectionService(providerClient, builder.Configuration));
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        var selected = new ProposalConnection { Model = "alpha" };
        using var response = await client.PostAsJsonAsync($"/api/proposals/{action}", new ProposalConnectionRequest(selected), ProposalJsonContext.Default.ProposalConnectionRequest);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync(ProposalJsonContext.Default.ProposalConnectionStatus);
        result!.Connection.ShouldBe(selected);
        result.Error.ShouldBeNull();
        result.Models.ShouldBe(["alpha", "beta"]);
        await app.StopAsync();
    }

    private sealed class ProviderHandler : HttpMessageHandler
    {
        public string? Failure { get; init; }
        public Uri? LastUri { get; private set; }
        public string? ChatBody { get; private set; }
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            Token = cancellationToken;
            Started.TrySetResult();
            if (Failure == "wait") await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Failure == "timeout") throw new TaskCanceledException();
            if (Failure == "unavailable") return new(HttpStatusCode.ServiceUnavailable);
            if (Failure == "malformed") return new(HttpStatusCode.OK) { Content = new StringContent("not json") };
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { models = new[] { new { name = "beta" }, new { name = "alpha" }, new { name = "alpha" } } }) };
            ChatBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { message = new { content = Failure == "empty" ? "" : "OK" } }) };
        }
    }
}
