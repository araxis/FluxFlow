using System.Net;
using FluxFlow.StudioWebHost;
using Microsoft.AspNetCore.TestHost;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class WindowsServiceEndpointTests
{
    [Fact]
    public async Task Starting_an_absent_service_reaches_the_packaged_cli_and_reports_the_service_name()
    {
        await using var host = StudioHost.Create([], builder => builder.WebHost.UseTestServer());
        await host.StartAsync();
        using var client = host.GetTestClient();
        var name = $"FluxFlow-test-{Guid.NewGuid():N}";
        var url = $"/api/runs/windows-service/start?name={name}&targetMajor=11&automatic=false";
        using var denied = await client.PostAsync(url, null);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Add("X-FluxFlow-Run", "1");
        using var result = await client.PostAsync(url, null);
        result.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var message = await result.Content.ReadAsStringAsync();
        if (OperatingSystem.IsWindows()) message.ShouldContain(name);
        else message.ShouldContain("Windows Studio host");
    }
}
