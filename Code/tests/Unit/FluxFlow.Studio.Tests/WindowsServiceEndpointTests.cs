using System.Net;
using FluxFlow.StudioWebHost;
using Microsoft.AspNetCore.TestHost;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class WindowsServiceEndpointTests
{
    [Fact]
    public async Task Local_service_endpoint_checks_the_run_header_and_validates_the_name()
    {
        await using var host = StudioHost.Create([], builder => builder.WebHost.UseTestServer());
        await host.StartAsync();
        using var client = host.GetTestClient();
        const string url = "/api/runs/service/uninstall?name=invalid%2Fname&targetMajor=11&automatic=false";
        using var denied = await client.PostAsync(url, null);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Add("X-FluxFlow-Run", "1");
        using var invalid = await client.PostAsync(url, null);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync()).ShouldContain("service name");
    }

    [Theory]
    [InlineData("start")]
    [InlineData("uninstall")]
    public async Task An_absent_service_reaches_the_packaged_cli_and_reports_the_service_name(string action)
    {
        await using var host = StudioHost.Create([], builder => builder.WebHost.UseTestServer());
        await host.StartAsync();
        using var client = host.GetTestClient();
        var name = $"FluxFlow-test-{Guid.NewGuid():N}";
        var url = $"/api/runs/windows-service/{action}?name={name}&targetMajor=11&automatic=false";
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
