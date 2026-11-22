using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class HttpConnectionEditorTests
{
    [Theory]
    [InlineData("BaseAddress", "https://service.example/path?x=1", true)]
    [InlineData("BaseAddress", "ftp://service.example", false)]
    [InlineData("BaseAddress", "https://user@service.example/path", false)]
    [InlineData("TokenEndpoint", "http://localhost/token", true)]
    [InlineData("TokenEndpoint", "http://remote.example/token", false)]
    [InlineData("TokenEndpoint", "https://remote.example/token#fragment", false)]
    public void Field_and_whole_HTTP_connection_agree_on_address_rules(string name, string address, bool valid)
    {
        var editor = StudioTestServices.HttpConnection();
        var settings = editor.Create();
        var field = editor.PropertyGroups!(settings).SelectMany(group => group.Options).Single(option => option.Name == name);
        field.ValidateText!(null).ShouldBeNull();
        (field.ValidateText(address) is null).ShouldBe(valid);
        if (name == "BaseAddress") settings[name] = address;
        else settings["Authentication"] = new JsonObject { ["Scheme"] = "OAuthClientCredentials",
            ["TokenEndpoint"] = address, ["ClientId"] = "fixture", ["ClientSecret"] = "fixture-only" };
        if (valid) Should.NotThrow(() => editor.Validate!(settings)); else Should.Throw<ArgumentException>(() => editor.Validate!(settings));
    }

    [Fact]
    public void Registration_checks_repairable_auth_conflicts_without_rewriting_the_HTTP_connection()
    {
        var editor = StudioTestServices.HttpConnection();
        var settings = JsonNode.Parse("""{"Type":"http.client","Authentication":{"Scheme":"bAsIc","Username":"example","Password":"","Token":"fixture-only"},"Retry":{"BackoffType":1},"Extra":{"Keep":42}}""")!.AsObject();
        var before = settings.DeepClone();
        Should.Throw<ArgumentException>(() => editor.Validate!(settings));
        JsonNode.DeepEquals(settings, before).ShouldBeTrue();
        settings["Authentication"]!.AsObject().Remove("Token");
        before = settings.DeepClone();
        Should.NotThrow(() => editor.Validate!(settings));
        JsonNode.DeepEquals(settings, before).ShouldBeTrue();
    }

    [Fact]
    public void Invalid_HTTP_header_values_are_not_quoted_in_the_advanced_editor_error()
    {
        var editor = StudioTestServices.HttpConnection(); var settings = editor.Create();
        settings["Headers"] = new JsonObject { ["Max-Forwards"] = "sensitive-fixture-value" };
        var error = Should.Throw<ArgumentException>(() => editor.Validate!(settings));
        error.Message.ShouldNotContain("sensitive-fixture-value");
    }

    [Theory]
    [InlineData("Timeout", "00:00:00", false)]
    [InlineData("Timeout", "24.20:31:23.647", true)]
    [InlineData("Timeout", "24.20:31:23.648", false)]
    [InlineData("PooledConnectionLifetime", "00:00:00", false)]
    [InlineData("PooledConnectionLifetime", "00:00:00.0000001", true)]
    [InlineData("Delay", "1.00:00:00", true)]
    [InlineData("Delay", "1.00:00:01", false)]
    [InlineData("MaxDelay", "1.00:00:01", false)]
    public void HTTP_duration_bounds_match_registration(string name, string value, bool valid)
    {
        var editor = StudioTestServices.HttpConnection(); var settings = editor.Create();
        var field = editor.PropertyGroups!(settings).SelectMany(group => group.Options).Single(item => item.Name == name);
        var duration = TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        (duration >= field.MinDuration && (field.MaxDuration is null || duration <= field.MaxDuration)).ShouldBe(valid);
        if (name is "Delay" or "MaxDelay") settings["Retry"] = new JsonObject { [name] = value };
        else settings[name] = value;
        if (valid) Should.NotThrow(() => editor.Validate!(settings));
        else if (name is "Delay" or "MaxDelay") Should.Throw<System.ComponentModel.DataAnnotations.ValidationException>(() => editor.Validate!(settings));
        else Should.Throw<ArgumentOutOfRangeException>(() => editor.Validate!(settings));
    }
}
