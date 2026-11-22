using System.Reflection;
using System.Text.Json;
using FluxFlow.Plugins;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace FluxFlow.Environments.Tests;

public sealed class SerialPortableDesignTests
{
    [Theory]
    [InlineData("serial.reader")]
    [InlineData("serial.writer")]
    public void Frame_size_feedback_survives_an_unrelated_invalid_saved_setting(string type)
    {
        var component = new FluxFlow.Plugins.Serial.SerialPlugin().Description.Components.Single(c => c.Palette.ComponentType == type);
        var rules = new FluxFlow.Plugins.Serial.SerialDesign().Component(component);
        const string json = """{"fRaMiNg":2,"FrameLength":"32","MaxFrameBytes":16,"ReadBufferBytes":8,"Encoding":"unknown-future-value"}""";
        var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        rules.FieldErrors!(settings).Keys.ShouldBe(["FrameLength"]);
        settings["fRaMiNg"] = JsonSerializer.SerializeToElement("Raw");
        rules.FieldErrors(settings).ShouldBeEmpty();
        settings["ReadBufferBytes"] = JsonSerializer.SerializeToElement(32);
        rules.FieldErrors(settings).Keys.ShouldBe(["ReadBufferBytes"]);
        settings["ReadBufferBytes"] = JsonSerializer.SerializeToElement(int.MaxValue);
        rules.FieldErrors(settings).ShouldBeEmpty(); // The integer editor reports its own range error.
        settings["Encoding"].GetString().ShouldBe("unknown-future-value");
    }

    [Fact]
    public void Headless_host_can_interpret_saved_frames_and_port_suggestions_without_a_view_or_driver()
    {
        var wire = JsonSerializer.Serialize(new[] { new FluxFlow.Plugins.Serial.SerialPlugin().Description },
            PluginJsonContext.Default.PluginDescriptionArray);
        var catalog = JsonSerializer.Deserialize(wire, PluginJsonContext.Default.PluginDescriptionArray)!.Single();
        catalog.StudioEntry.ShouldBeNull();
        var entry = catalog.Design.ShouldNotBeNull();
        var assembly = Assembly.Load(entry.Assembly);
        var design = (IPluginDesign)Activator.CreateInstance(assembly.GetType(entry.Type, true)!)!;
        var visited = new HashSet<string>();
        var pending = new Queue<Assembly>([assembly]);
        while (pending.TryDequeue(out var current))
        {
            if (!visited.Add(current.FullName!)) continue;
            foreach (var reference in current.GetReferencedAssemblies())
            {
                var name = reference.Name!;
                name.ShouldNotBe("System.IO.Ports");
                name.ShouldNotContain("Studio");
                name.ShouldNotContain("MudBlazor");
                name.ShouldNotContain("AspNetCore.Components");
                name.ShouldNotContain("Avalonia");
                if (name.StartsWith("FluxFlow.", StringComparison.Ordinal)) pending.Enqueue(Assembly.Load(reference));
            }
        }
        var port = catalog.Connections.Single();
        var address = port.Sections.SelectMany(s => s.Options).Single(f => f.Name == "PortName");
        address.Suggestions!.Operation.ShouldBe("ports");
        using var connection = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string,string?> { ["PortName"] = "/remote/port" }).Build();
        design.Connection(port).Validate!(connection);
        foreach (var frame in catalog.Components.Where(c => c.Editor == "properties"))
        {
            var fields = frame.Inspector.Sections.Single().Options;
            var delimiter = fields.Single(f => f.Name == "DelimiterBase64").VisibleWhen!;
            var length = fields.Single(f => f.Name == "FrameLength").VisibleWhen!;
            var values = new Dictionary<string, JsonElement>();
            delimiter.Matches(fields, values).ShouldBeTrue();
            length.Matches(fields, values).ShouldBeFalse();
            values["fRaMiNg"] = JsonSerializer.SerializeToElement(2);
            length.Matches(fields, values).ShouldBeTrue();
            delimiter.Matches(fields, values).ShouldBeFalse();
            values["fRaMiNg"] = JsonSerializer.SerializeToElement("future-mode");
            length.Matches(fields, values).ShouldBeTrue();
            delimiter.Matches(fields, values).ShouldBeTrue();
            values["fRaMiNg"] = JsonSerializer.SerializeToElement("Raw");
            values["ReadBufferBytes"] = JsonSerializer.SerializeToElement(32);
            values["MaxFrameBytes"] = JsonSerializer.SerializeToElement(16);
            design.Component(frame).FieldErrors!(values).ContainsKey("ReadBufferBytes").ShouldBeTrue();
        }
        catalog.Components.Count(c => c.Editor == "properties").ShouldBe(2);
    }
}
