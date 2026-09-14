using System.Diagnostics;
using System.Text.Json;
using FluxFlow.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Observability.Tests;

public sealed class TraceStreamTests
{
    [Fact]
    public async Task Slow_subscriber_disconnects_without_blocking_other_subscribers()
    {
        using var stream = new FlowTraceStream();
        using var source = new ActivitySource(FlowDiagnostics.ListenerName);
        await using var slow = stream.ReadAsync(1).GetAsyncEnumerator();
        await using var fast = stream.ReadAsync(1).GetAsyncEnumerator();
        var s = slow.MoveNextAsync().AsTask();
        var f = fast.MoveNextAsync().AsTask();
        Emit(source, "first");
        (await s).ShouldBeTrue();
        (await f).ShouldBeTrue();
        f = fast.MoveNextAsync().AsTask();
        Emit(source, "second");
        (await f).ShouldBeTrue();
        f = fast.MoveNextAsync().AsTask();
        Emit(source, "third");
        (await f).ShouldBeTrue();
        fast.Current.Name.ShouldBe("third");
        stream.ConnectedClients.ShouldBe(1);
        (await slow.MoveNextAsync()).ShouldBeTrue();
        slow.Current.Name.ShouldBe("second");
        (await slow.MoveNextAsync()).ShouldBeFalse();
        stream.Dispose();
        (await fast.MoveNextAsync()).ShouldBeFalse();
        stream.ConnectedClients.ShouldBe(0);
    }

    [Fact]
    public async Task Endpoint_sends_named_json_events_and_releases_client_on_disconnect()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddFluxFlowTraceStream();
        await using var app = builder.Build();
        app.MapFluxFlowTraceStream();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var responseTask = client.GetAsync("/fluxflow/traces", HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
        var stream = app.Services.GetRequiredService<FlowTraceStream>();
        while (stream.ConnectedClients == 0) await Task.Delay(5, cancellation.Token);
        using var source = new ActivitySource(FlowDiagnostics.ListenerName);
        Emit(source, "test");
        using var response = await responseTask;
        response.EnsureSuccessStatusCode();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellation.Token));
        var lines = new List<string>();
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellation.Token);
            if (string.IsNullOrEmpty(line)) break;
            lines.Add(line);
        }
        lines.ShouldContain("event: fluxflow.span");
        var data = lines.Single(line => line.StartsWith("data: "))[6..];
        using var json = JsonDocument.Parse(data);
        json.RootElement.GetProperty("name").GetString().ShouldBe("test");
        json.RootElement.GetProperty("traceId").GetString().ShouldBe("1234567890abcdef1234567890abcdef");
        json.RootElement.GetProperty("parentSpanId").GetString().ShouldBe("1234567890abcdef");
        json.RootElement.GetProperty("events")[0].GetProperty("name").GetString().ShouldBe("exception");
        cancellation.Cancel();
        response.Dispose();
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (stream.ConnectedClients != 0) await Task.Delay(5, shutdown.Token);
        await app.StopAsync(shutdown.Token);
    }

    private static void Emit(ActivitySource source, string name)
    {
        using var activity = source.StartActivity(name, ActivityKind.Consumer,
            new ActivityContext(ActivityTraceId.CreateFromString("1234567890abcdef1234567890abcdef"),
                ActivitySpanId.CreateFromString("1234567890abcdef"), ActivityTraceFlags.Recorded));
        activity.ShouldNotBeNull();
        activity.SetStatus(ActivityStatusCode.Error);
        activity.AddEvent(new ActivityEvent("exception"));
    }
}
