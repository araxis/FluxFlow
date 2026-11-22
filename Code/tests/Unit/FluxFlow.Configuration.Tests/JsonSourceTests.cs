using FluxFlow.Configuration.Application;
using FluxFlow.Configuration.Sources;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Source;
using FluxFlow.Configuration.Components.Sources;
using FluxFlow.Application;
using FluxFlow.Core.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluxFlow.Configuration.Tests;

public sealed class JsonSourceTests
{
    [Fact]
    public void Published_relative_paths_work_with_both_separators_and_prefer_a_native_file()
    {
        var root = Directory.CreateTempSubdirectory("source-path-");
        try
        {
            var data = root.CreateSubdirectory("data");
            File.WriteAllText(Path.Combine(data.FullName, "orders.json"), "[23]");
            var reference = new JsonSource(@"file:data\orders.json");
            reference.Read(root.FullName).GetRawText().ShouldBe("[23]");
            new JsonSource("data/orders.json").Read(root.FullName).GetRawText().ShouldBe("[23]");
            if (Path.DirectorySeparatorChar == '/')
            {
                File.WriteAllText(Path.Combine(root.FullName, @"data\orders.json"), "[99]");
                reference.Read(root.FullName).GetRawText().ShouldBe("[99]");
            }
        }
        finally { root.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("[123,\"123\",true,\"true\",null,{},[]]", "[123,\"123\",true,\"true\",null,{},[]]")]
    [InlineData("inline:{\"name\":\"سلام\"}", "{\"name\":\"سلام\"}")]
    [InlineData("INLINE:\"file:sample.json\"", "\"file:sample.json\"")]
    [InlineData("null", "null")]
    [InlineData("123", "123")]
    [InlineData("true", "true")]
    [InlineData("\"samples.json\"", "\"samples.json\"")]
    public void Native_binder_reads_a_source_and_preserves_JSON(string source, string expected)
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Values"] = source }).Build();
        var model = configuration.Get<SourceConfiguration>()!;
        model.Values!.Read(AppContext.BaseDirectory).GetRawText().ShouldBe(expected);
    }

    [Theory]
    [InlineData("inline:")]
    [InlineData("[invalid")]
    [InlineData("{invalid")]
    [InlineData("\"unclosed")]
    [InlineData("inline:sample.json")]
    public void Malformed_JSON_is_not_reinterpreted_as_a_path(string source)
        => Should.Throw<JsonException>(() => new JsonSource(source).Read(AppContext.BaseDirectory));

    [Theory]
    [InlineData("file:samples.json")]
    [InlineData("samples.json")]
    [InlineData("./samples.json")]
    public async Task File_values_are_snapshotted_at_registration_and_reused_across_builds(string sourcePath)
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluxflow-json-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "samples.json");
        try
        {
            const string values = "[123,\"123\",true,\"true\",null,{\"name\":\"سلام\"},[]]";
            // A UTF-8 BOM is common in files written by desktop editors.
            File.WriteAllText(file, values, new UTF8Encoding(true));
            using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["workflows:main:samples:Type"] = "source",
                    ["workflows:main:samples:Values"] = sourcePath
                }).Build();
            var services = new ServiceCollection().AddSource(directory);
            services.AddFluxFlowApplication(configuration);
            File.Delete(file);
            configuration["workflows:main:samples:Values"] = "file:now-missing.json";
            using var expected = JsonDocument.Parse(values);
            for (var run = 0; run < 2; run++)
            {
                await using var provider = services.BuildServiceProvider();
                await using var app = await provider.BuildApplicationAsync();
                var source = provider.GetWorkflowServices("main").GetRequiredKeyedService<FluxFlow.Core.Nodes.IFlowNode>("main.samples").ShouldBeOfType<Source>();
                var sink = new BufferBlock<FlowMessage>();
                using var link = source.Output.LinkTo(sink);
                // Exercise the native component without adding application routes or starting sources.
                foreach (var item in expected.RootElement.EnumerateArray())
                {
                    await source.Input.SendAsync(FlowMessage.Create(JsonFlowValue.Null));
                    var message = await sink.ReceiveAsync(TimeSpan.FromSeconds(5));
                    message.Value.Json.ValueKind.ShouldBe(item.ValueKind);
                    message.Value.Json.GetRawText().ShouldBe(item.GetRawText());
                }
                (await provider.BuildApplicationAsync()).ShouldBeSameAs(app);
            }
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData("file:")]
    [InlineData("file:missing.json")]
    [InlineData("missing.json")]
    [InlineData("inline:{}")]
    public void Invalid_source_rejects_registration_atomically(string source)
    {
        using var configuration = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["workflows:main:samples:Type"] = "source",
                ["workflows:main:samples:Values"] = source
            }).Build();
        var services = new ServiceCollection().AddSource();
        var before = services.ToArray();
        var error = Should.Throw<InvalidOperationException>(() => services.AddFluxFlowApplication(configuration));
        error.Message.ShouldContain("main.samples");
        services.ToArray().ShouldBe(before);
    }

    [Theory]
    [InlineData("samples")]
    [InlineData("123.json")]
    [InlineData("sample data.txt")]
    public void Paths_do_not_require_an_extension_and_file_JSON_errors_are_preserved(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluxflow-json-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, name);
        try
        {
            File.WriteAllText(file, "[1,\"1\"]");
            new JsonSource(name).Read(directory).GetRawText().ShouldBe("[1,\"1\"]");
            new JsonSource(file).Read(directory).GetRawText().ShouldBe("[1,\"1\"]");
            File.WriteAllText(file, "[invalid");
            Should.Throw<JsonException>(() => new JsonSource(name).Read(directory));
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void Valid_JSON_wins_over_a_filename_and_prefixes_resolve_ambiguity()
    {
        var directory = Path.Combine(Path.GetTempPath(), "fluxflow-json-ambiguity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "123");
        try
        {
            File.WriteAllText(file, "[42]");
            new JsonSource("123").Read(directory).GetInt32().ShouldBe(123);
            new JsonSource("file:123").Read(directory).GetRawText().ShouldBe("[42]");
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
