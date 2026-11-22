using System.Net;
using System.Net.Http.Headers;
using FluxFlow.StudioWebHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class DictationEndpointTests
{
    [Theory]
    [InlineData("Local", "http://127.0.0.1:8080/v1/audio/transcriptions", true)]
    [InlineData("Local", "https://speech.example/transcribe", false)]
    [InlineData("Remote", "https://speech.example/transcribe", true)]
    [InlineData("Remote", "http://speech.example/transcribe", false)]
    [InlineData("Local", "", false)]
    public async Task Dictation_uses_only_the_explicit_host_endpoint_and_keeps_credentials_on_the_host(string location, string endpoint, bool allowed)
    {
        var handler = new TranscriptionHandler();
        await using var app = StudioHost.Create([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dictation:Local:Endpoint"] = "http://localhost:8081/transcribe",
                ["Dictation:Remote:Endpoint"] = "https://other.example/transcribe",
                ["Dictation:Local:ApiKey"] = "local-secret",
                ["Dictation:Remote:ApiKey"] = "remote-secret",
            });
            builder.Configuration[$"Dictation:{location}:Endpoint"] = endpoint;
            builder.Services.AddHttpClient("Dictation").ConfigurePrimaryHttpMessageHandler(() => handler);
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var form = Audio(location);
        using var response = await client.PostAsync("/api/dictation/transcribe", form);
        response.StatusCode.ShouldBe(allowed ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);
        handler.Calls.ShouldBe(allowed ? 1 : 0);
        if (allowed)
        {
            (await response.Content.ReadAsStringAsync()).ShouldBe("Keep CASE. سلام");
            handler.Endpoint.ShouldBe(endpoint);
            handler.Authorization.ShouldBe(location == "Local" ? "Bearer local-secret" : "Bearer remote-secret");
            handler.Upload.ShouldNotBeNull().ShouldContain("selected-transcriber");
            handler.Upload!.ShouldContain("name=file");
        }
    }

    [Theory]
    [InlineData("Local", null, "auto")]
    [InlineData("Local", "auto", "auto")]
    [InlineData("Local", "fa", "fa")]
    [InlineData("Local", "en", "en")]
    [InlineData("Remote", null, null)]
    [InlineData("Remote", "auto", null)]
    [InlineData("Remote", "fa", "fa")]
    [InlineData("Remote", "en", "en")]
    public async Task Language_is_forwarded_without_changing_provider_selection(string location, string? language, string? expected)
    {
        var handler = new TranscriptionHandler();
        await using var app = StudioHost.Create([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration["Dictation:Local:Endpoint"] = "http://localhost:8080/transcribe";
            builder.Configuration["Dictation:Remote:Endpoint"] = "https://speech.example/transcribe";
            builder.Services.AddHttpClient("Dictation").ConfigurePrimaryHttpMessageHandler(() => handler);
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var form = Audio(location);
        if (language is not null) form.Add(new StringContent(language), "Language");
        using var response = await client.PostAsync("/api/dictation/transcribe", form);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        handler.Language.ShouldBe(expected);
        handler.Endpoint.ShouldBe(location == "Local" ? "http://localhost:8080/transcribe" : "https://speech.example/transcribe");
    }

    [Theory]
    [InlineData("")]
    [InlineData("unsupported")]
    public async Task Invalid_language_is_rejected_before_contacting_the_provider(string language)
    {
        var handler = new TranscriptionHandler();
        await using var app = StudioHost.Create([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration["Dictation:Local:Endpoint"] = "http://localhost:8080/transcribe";
            builder.Services.AddHttpClient("Dictation").ConfigurePrimaryHttpMessageHandler(() => handler);
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var form = Audio("Local");
        form.Add(new StringContent(language), "Language");
        using var response = await client.PostAsync("/api/dictation/transcribe", form);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        handler.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("{\"text\":123}")]
    [InlineData("{}")]
    [InlineData("not JSON")]
    public async Task Invalid_transcription_responses_return_a_controlled_error(string body)
    {
        var handler = new TranscriptionHandler(body: body);
        await using var app = StudioHost.Create([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration["Dictation:Local:Endpoint"] = "http://localhost:8080/transcribe";
            builder.Services.AddHttpClient("Dictation").ConfigurePrimaryHttpMessageHandler(() => handler);
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var form = Audio("Local");
        using var response = await client.PostAsync("/api/dictation/transcribe", form);
        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_reaches_transcription_and_never_retries_another_provider()
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new TranscriptionHandler(entered);
        await using var app = StudioHost.Create([], builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Configuration["Dictation:Local:Endpoint"] = "http://localhost:8080/transcribe";
            builder.Services.AddHttpClient("Dictation").ConfigurePrimaryHttpMessageHandler(() => handler);
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var cancellation = new CancellationTokenSource();
        using var form = Audio("Local");
        var sending = client.PostAsync("/api/dictation/transcribe", form, cancellation.Token);
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(async () => await sending);
        token.IsCancellationRequested.ShouldBeTrue();
        handler.Calls.ShouldBe(1);
    }

    private static MultipartFormDataContent Audio(string location)
    {
        var form = new MultipartFormDataContent();
        var audio = new ByteArrayContent([1, 2, 3]);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/webm");
        form.Add(audio, "Audio", "recording.webm");
        form.Add(new StringContent(location), "Location");
        form.Add(new StringContent("selected-transcriber"), "Model");
        return form;
    }

    private sealed class TranscriptionHandler(TaskCompletionSource<CancellationToken>? entered = null, string body = "{\"text\":\"Keep CASE. سلام\"}") : HttpMessageHandler
    {
        public int Calls;
        public string? Endpoint, Authorization, Upload, Language;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Endpoint = request.RequestUri!.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            Upload = await request.Content!.ReadAsStringAsync(cancellationToken);
            var language = ((MultipartFormDataContent)request.Content).SingleOrDefault(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "language");
            Language = language is null ? null : await language.ReadAsStringAsync(cancellationToken);
            if (entered is not null)
            {
                entered.SetResult(cancellationToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
}
