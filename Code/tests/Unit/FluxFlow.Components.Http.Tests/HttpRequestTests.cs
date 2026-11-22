using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Application;
using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Components.Http;
using FluxFlow.Configuration.Components.Mapping;
using FluxFlow.Core.Messaging;
using FluxFlow.Expressions.Jsonata;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using NativeClient = System.Net.Http.HttpClient;

namespace FluxFlow.Components.Http.Tests;

public sealed partial class HttpTests
{
    [Theory]
    [InlineData("{\"value\":42}")]
    [InlineData("[1,2,3]")]
    [InlineData("123")]
    [InlineData("false")]
    [InlineData("null")]
    public async Task Structured_request_bodies_are_sent_as_json(string body)
    {
        using var handler = new Handler(async (request, token) =>
        {
            request.Content.ShouldNotBeNull();
            request.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
            (await request.Content.ReadAsStringAsync(token)).ShouldBe(body);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new NativeClient(handler);
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        using var document = JsonDocument.Parse($$"""{"requestUrl":"https://example.test/","method":"POST","body":{{body}}} """);
        await component.Input.SendAsync(FlowMessage.Create(new JsonFlowValue(document.RootElement)));
        var result = await output.ReceiveAsync(Deadline);
        result.IsError.ShouldBeFalse(result.Error?.Message);
        handler.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Absent_typed_body_stays_absent_when_converted_to_json(bool convert)
    {
        using var handler = new Handler((request, _) =>
        {
            request.Content.ShouldBeNull();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        using var client = new NativeClient(handler);
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        var request = new HttpSendRequest { RequestUrl = "https://example.test/" };
        FlowValue value = convert ? new JsonFlowValue(request.Json) : request;
        await component.Input.SendAsync(FlowMessage.Create(value));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Json_encoding_can_send_a_json_string_literal()
    {
        using var handler = new Handler(async (request, token) =>
        {
            (await request.Content!.ReadAsStringAsync(token)).ShouldBe("\"hello\"");
            request.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new NativeClient(handler);
        await using var component = new HttpClientComponent(new() { Client = client });
        var output = Capture(component.Output);
        await component.Input.SendAsync(Message(new { requestUrl = "https://example.test/", method = "POST", body = "hello", encoding = "Json" }));
        (await output.ReceiveAsync(Deadline)).IsError.ShouldBeFalse();
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Jsonata_mapper_builds_dynamic_request_body_headers_method_and_url()
    {
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await Server.Start(async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            received.SetResult([context.Request.Method, context.Request.Path, context.Request.ContentType!,
                context.Request.Headers["X-Order"].ToString(), await reader.ReadToEndAsync()]);
            context.Response.StatusCode = 201;
            await context.Response.WriteAsJsonAsync(new { accepted = true });
        });
        var json = $$"""
        {
          "Resources": { "api": { "Type": "http.client", "BaseAddress": "{{server.Address}}" } },
          "Workflows": { "demo": {
            "map": { "Type": "mapper", "Engine": "jsonata",
              "Expression": "{ 'method': 'POST', 'requestUrl': 'orders/' & orderId, 'headers': { 'X-Order': orderId }, 'body': { 'id': orderId, 'quantity': quantity }, 'timeout': '00:00:05' }",
              "Output": "send.Input" },
            "send": { "Type": "http.client", "Client": "api" }
          } }
        }
        """;
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
        var services = new ServiceCollection().AddHttp().AddMapper().AddJsonataExpressionEngine();
        services.AddFluxFlowApplication(config);
        await using var provider = services.BuildServiceProvider();
        await using var app = await provider.BuildApplicationAsync();
        var output = Capture(provider.GetWorkflowServices("demo").GetRequiredKeyedService<ISourceBlock<FlowMessage>>("demo.send.Output"));
        await app.StartAsync();
        await provider.GetWorkflowServices("demo").GetRequiredKeyedService<ITargetBlock<FlowMessage>>("demo.map.Input")
            .SendAsync(Message(new { orderId = "A12", quantity = 3 }));
        var response = await output.ReceiveAsync(Deadline);
        response.IsError.ShouldBeFalse(response.Error?.Message);
        response.Value.ShouldBeOfType<HttpResponse>().StatusCode.ShouldBe(201);
        (await received.Task.WaitAsync(Deadline)).ShouldBe(["POST", "/orders/A12", "application/json", "A12", "{\"id\":\"A12\",\"quantity\":3}"]);
        await app.StopAsync().WaitAsync(Deadline);
    }
}
