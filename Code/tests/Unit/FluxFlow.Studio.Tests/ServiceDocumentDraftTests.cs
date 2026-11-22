using FluxFlow.Environments;
using System.Text.Json.Nodes;
using FluxFlow.Studio.UI.Features.Environments.Services.Documents;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed class ServiceDocumentDraftTests
{
    private static ServiceNameList EnabledPlugins => FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpSetup.Provider.StartupFiles.Single(f => f.Format == ServiceDocumentKind.NameList).Names!;

    [Theory]
    [InlineData(ServiceDocumentKind.Variables, "{ \"Case\": \" value \", \"Empty\": \"\" }")]
    [InlineData(ServiceDocumentKind.Files, "{ \"/etc/test\": \"line\\nnext\\r\\n\" }")]
    [InlineData(ServiceDocumentKind.KeyValueLines, "# keep comment\r\nlisteners.tcp.default=5672\r\ncustom.value = 'abc#def' # comment\r\n")]
    [InlineData(ServiceDocumentKind.NameList, " [ rabbitmq_management, rabbitmq_mqtt ].\r\n")]
    [InlineData(ServiceDocumentKind.ObjectCollections, "{\"rabbit_version\":\"4.1.0\", \"custom\":{\"nested\":[1,true,null]},\"queues\":[]}")]
    public void Opening_and_saving_without_edits_preserves_original_text(ServiceDocumentKind kind, string original)
        => new ServiceDocumentDraft(kind, original, FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpSetup.Provider.StartupFiles.Last().Templates!, EnabledPlugins).Serialize().ShouldBe(original);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Both_broker_providers_preserve_saved_lists_and_validate_visual_edits(bool mqtt)
    {
        var provider = mqtt ? FluxFlow.Plugins.Mqtt.RabbitMqMqttSetup.Provider : FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpSetup.Provider;
        var file = provider.StartupFiles.Single(f => f.Path == "/etc/rabbitmq/enabled_plugins");
        var names = file.Names.ShouldNotBeNull();
        var defaults = file.DefaultContent(new());
        names.Write(defaults, names.Read(defaults)).ShouldBe(defaults);
        const string original = " [ rabbitmq_management,\r\n rabbitmq_mqtt ] .\r\n";
        var draft = new ServiceDocumentDraft(file.Format!.Value, original, names: names);
        draft.Serialize().ShouldBe(original);
        draft.Entries.RemoveAt(0);
        draft.Serialize().ShouldBe("[rabbitmq_mqtt].\r\n");
        var longest = new string('a', 255);
        draft.Entries[0].Name = longest;
        names.Read(draft.Serialize()).ShouldBe(new[] { longest });
        draft.Entries[0].Name += "a";
        Should.Throw<ArgumentException>(() => draft.Serialize());
        draft.Entries[0].Name = "bad-name";
        Should.Throw<ArgumentException>(() => draft.Serialize());
        Should.Throw<ArgumentException>(() => names.Read("[rabbitmq_mqtt,]."));
        Should.Throw<ArgumentException>(() => names.Read("['custom-plugin']."));
    }

    [Fact]
    public void Mosquitto_acl_edits_only_the_selected_rule_and_keeps_scopes_in_order()
    {
        var file = FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider.StartupFiles.Single(f => f.Directives is not null);
        file.GeneratedWithUsers.ShouldBeTrue();
        var rules = file.Directives!;
        const string original = "\uFEFF# saved\r\ntopic deny\nuser  test user\rtopic write   readings/#  \r\n" +
            "# same rule under a different user\nuser next\r\ntopic write   readings/#\npattern read devices/%c/%u/+";
        var draft = new ServiceDocumentDraft(file.Format!.Value, original, directives: rules);
        draft.Serialize().ShouldBe(original);
        var first = rules.Fields(draft.Entries[0]);
        first.OfType<DocumentChoice>().Single(f => f.Label == "Access").Read().ShouldBe("readwrite");
        first.OfType<DocumentText>().Single().Read().ShouldBe("deny");
        var selected = rules.Fields(draft.Entries[2]);
        selected.OfType<DocumentChoice>().Single(f => f.Label == "Access").Write("deny");
        selected.OfType<DocumentText>().Single().Write("rooms with spaces/+");
        var edited = original.Replace("write   readings/#  ", "deny   rooms with spaces/+  ");
        draft.Serialize().ShouldBe(edited);
        var added = rules.Create();
        draft.Entries.Add(added);
        Should.Throw<ArgumentException>(() => draft.Serialize());
        rules.Fields(added).OfType<DocumentText>().Single().Write("private/#");
        draft.Serialize().ShouldBe(edited + "\r\ntopic read private/#\r\n");
        draft.Entries.Remove(added);
        draft.Serialize().ShouldBe(edited);
    }

    [Theory]
    [InlineData("topic", "deny", "")]
    [InlineData("pattern", "devices/%u/#", "")]
    [InlineData("topic", "rooms/+", "readwrite   ")]
    public void Mosquitto_acl_restores_permission_spelling_after_reverting_edits(string kind, string filter, string permission)
    {
        var file = FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider.StartupFiles.Single(f => f.Directives is not null);
        var original = $"\uFEFF# scope\r\nuser alice\n{kind}  {permission}{filter} \t\r\nuser bob";
        var draft = new ServiceDocumentDraft(file.Format!.Value, original, directives: file.Directives);
        var fields = file.Directives!.Fields(draft.Entries[1]);
        var access = fields.OfType<DocumentChoice>().Single(f => f.Label == "Access");
        access.Write("write");
        var gap = permission.Length == 0 ? " " : permission["readwrite".Length..];
        draft.Serialize().ShouldBe(original.Replace($"{kind}  {permission}{filter}", $"{kind}  write{gap}{filter}"));
        access.Write("readwrite");
        draft.Serialize().ShouldBe(original);
        var target = fields.OfType<DocumentText>().Single();
        target.Write("room name/+");
        draft.Serialize().ShouldContain($"{kind}  readwrite{gap}room name/+");
        target.Write(filter);
        draft.Serialize().ShouldBe(original);
    }

    [Theory]
    [InlineData("unknown value")]
    [InlineData("topic admin sensors/#")]
    [InlineData("topic read sensors/#/invalid")]
    [InlineData("pattern write sensors/prefix-%u")]
    public void Mosquitto_acl_unrepresentable_source_requires_advanced_text(string source)
    {
        var file = FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider.StartupFiles.Single(f => f.Directives is not null);
        Should.Throw<ArgumentException>(() => new ServiceDocumentDraft(file.Format!.Value, source, directives: file.Directives));
    }

    [Fact]
    public void Mosquitto_directives_preserve_repeated_names_argument_text_and_layout()
    {
        var file = FluxFlow.Plugins.Mqtt.MosquittoSetup.Provider.StartupFiles.Single(f => f.Path.EndsWith("/mosquitto.conf", StringComparison.Ordinal));
        var defaults = file.DefaultContent(new());
        new ServiceDocumentDraft(file.Format!.Value, defaults).Serialize().ShouldBe(defaults);
        const string original = "\uFEFF# LAN\r\nlistener    1883  \nlistener 1884 127.0.0.1\r" +
            "topic sensors/# out 1\r\nplugin_opt_label first = second";
        var draft = new ServiceDocumentDraft(file.Format.Value, original);
        draft.Entries.Select(e => e.Name).ShouldBe(new[] { "listener", "listener", "topic", "plugin_opt_label" });
        draft.Entries[2].Value.ShouldBe("sensors/# out 1");
        draft.Serialize().ShouldBe(original);
        draft.Entries[1].Value = "1885 127.0.0.1";
        draft.Serialize().ShouldBe(original.Replace("1884", "1885"));
        draft.Entries.RemoveAt(0);
        draft.Entries.Add(new() { Name = "log_type", Value = "error" });
        draft.Entries.Add(new() { Name = "log_type", Value = "notice" });
        draft.Serialize().ShouldBe(original.Replace("listener    1883  \n", "").Replace("1884", "1885") +
            "\r\nlog_type error\r\nlog_type notice\r\n");
        draft.Entries[^1].Value = "notice\nlistener 1886";
        Should.Throw<ArgumentException>(() => draft.Serialize());
    }

    [Fact]
    public void Configuration_edits_preserve_other_lines_comments_and_line_endings()
    {
        var draft = new ServiceDocumentDraft(ServiceDocumentKind.KeyValueLines, "# keep\r\nlisteners.tcp.default=5672\r\ncustom.value = 'abc#def' # comment\r\n");
        draft.Entries[0].Value = "5673";
        draft.Entries.Add(new() { Name = "new.setting", Value = "true" });
        draft.Serialize().ShouldBe("# keep\r\nlisteners.tcp.default=5673\r\ncustom.value = 'abc#def' # comment\r\nnew.setting = true\r\n");
        draft.Entries.RemoveAt(0);
        draft.Serialize().ShouldNotContain("listeners.tcp.default");
        draft.Serialize().ShouldContain("# keep\r\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Broker_configuration_edits_change_only_the_value_and_preserve_inline_comments(bool mqtt)
    {
        var provider = mqtt ? FluxFlow.Plugins.Mqtt.RabbitMqMqttSetup.Provider : FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpSetup.Provider;
        var file = provider.StartupFiles.Single(f => f.Path == "/etc/rabbitmq/rabbitmq.conf");
        const string source = "\uFEFFlisteners.tcp.default = 1234\t # keep\r\ncustom\\.key='a#b'  # note\nlast=a=b#final";
        var draft = new ServiceDocumentDraft(file.Format!.Value, source);
        draft.Entries.Select(row => row.Value).ToArray().ShouldBe(new[] { "1234", "'a#b'", "a=b" });
        draft.Serialize().ShouldBe(source);
        draft.Entries[0].Value = "5672";
        draft.Entries[1].Value = "'c#d'";
        draft.Serialize().ShouldBe(source.Replace("1234", "5672").Replace("'a#b'", "'c#d'"));
        draft.Entries[1].Value = "c#d";
        Should.Throw<ArgumentException>(() => draft.Serialize());
        draft.Entries[1].Value = "ok";
        draft.Entries[1].Name = "bad..key";
        Should.Throw<ArgumentException>(() => draft.Serialize());
    }

    [Fact]
    public void Configuration_row_edits_validate_keys_and_values_without_rewriting_source()
    {
        const string original = "\uFEFF\tkey \t=   ''\r# literal = comment\nother = a=b#c";
        var draft = new ServiceKeyValueDocument(original);
        draft.Serialize().ShouldBe(original);
        var row = draft.Entries[0];
        row.Name = "other";
        Should.Throw<ArgumentException>(() => draft.Serialize()).Message.ShouldContain("Duplicate");
        row.Name = "bad=key";
        Should.Throw<ArgumentException>(() => draft.Serialize());
        row.Name = "renamed";
        row.Value = "one\nmore = injected";
        Should.Throw<ArgumentException>(() => draft.Serialize());
        row.Value = "first";
        draft.Serialize().ShouldBe("\uFEFF\trenamed \t=   first\r# literal = comment\nother = a=b#c");
    }

    [Fact]
    public void Definitions_edits_preserve_unknown_fields_and_argument_types()
    {
        const string original = """{"policies":[{"name":"keep"}],"queues":[{"name":"old","vhost":"/","durable":false,"arguments":{"x-max-length":12},"custom":{"data":[null,true]}}]}""";
        var draft = new ServiceDocumentDraft(ServiceDocumentKind.ObjectCollections, original, FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpSetup.Provider.StartupFiles.Last().Templates!, EnabledPlugins);
        var collections = draft.Collections.ShouldNotBeNull();
        collections.Schema.Fields("queues", collections.Rows("queues").Single()).OfType<DocumentText>().Single(f => f.Label == "Name").Write("new");
        var saved = JsonNode.Parse(draft.Serialize())!;
        var expected = JsonNode.Parse(original)!;
        expected["queues"]![0]!["name"] = "new";
        JsonNode.DeepEquals(saved, expected).ShouldBeTrue();
    }

    [Theory]
    [InlineData(ServiceDocumentKind.Variables, "NAME", "NAME")]
    [InlineData(ServiceDocumentKind.Files, "/file", "/file")]
    [InlineData(ServiceDocumentKind.NameList, "rabbitmq_mqtt", "rabbitmq_mqtt")]
    public void Duplicate_entries_are_rejected_instead_of_overwriting_values(ServiceDocumentKind kind, string first, string second)
    {
        var draft = new ServiceDocumentDraft(kind, kind == ServiceDocumentKind.NameList ? "[]." : "{}", names: EnabledPlugins);
        draft.Entries.Add(new() { Name = first, Value = "first" });
        draft.Entries.Add(new() { Name = second, Value = "second" });
        Should.Throw<ArgumentException>(() => draft.Serialize()).Message.ShouldContain("Duplicate");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Collection_draft_reverts_to_exact_source_without_materializing_absent_sections(bool mqtt)
    {
        var provider = mqtt ? FluxFlow.Plugins.Mqtt.RabbitMqMqttSetup.Provider : FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpSetup.Provider;
        var schema = provider.StartupFiles.Single(f => f.Path == "/etc/rabbitmq/definitions.json").Templates!;
        const string source = "\uFEFF{\r\n \"queues\": [ {\"name\":\"\\u0061\", \"durable\":null, \"arguments\":{\"nested\":[1,true]}} ], \"users\":[]\n}";
        var draft = schema.Open(source);
        foreach (var section in new[] { "bindings", "users" }) draft.Remove(section, draft.Add(section));
        var queue = draft.Rows("queues").Single();
        schema.Fields("queues", queue).OfType<DocumentReadOnly>().Single().Label.ShouldBe("Durable");
        var name = schema.Fields("queues", queue).OfType<DocumentText>().Single(f => f.Label == "Name");
        name.Write("temporary"); name.Write("a");
        draft.Serialize().ShouldBe(source);
        var added = draft.Add("queues");
        schema.Fields("queues", added).OfType<DocumentFlag>().Single(f => f.Label == "Durable").Write(false);
        added["durable"]!.GetValue<bool>().ShouldBeFalse();
        schema.Create("queues")["durable"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Theory]
    [InlineData(ServiceDocumentKind.Variables, "Case")]
    [InlineData(ServiceDocumentKind.Files, "/Case")]
    public void String_map_preserves_original_json_after_reverting_an_edit(ServiceDocumentKind kind, string key)
    {
        var source = "\uFEFF{ \r\n \"" + key + "\" : \"\\u0061\\r\\nb\\nc\\rd\\n\", \"" + key.ToLowerInvariant() + "\" : \"\" }";
        var draft = new ServiceStringMapDocument(kind, source);
        var entry = draft.Entries[0];
        entry.Value.ShouldBe("a\r\nb\nc\rd\n");
        entry.Value = "modified";
        entry.Value = "a\r\nb\nc\rd\n";
        draft.Serialize().ShouldBe(source);
    }

    [Theory]
    [InlineData(ServiceDocumentKind.Files, "{\"/a\":null}")]
    [InlineData(ServiceDocumentKind.Files, "{\"a\":\"text\"}")]
    [InlineData(ServiceDocumentKind.Variables, "{\"A=B\":\"value\"}")]
    [InlineData(ServiceDocumentKind.Variables, "{\"A\":\"first\",\"\\u0041\":\"second\"}")]
    [InlineData(ServiceDocumentKind.Variables, "{\"A\\nB\":\"value\"}")]
    public void Applying_invalid_source_cannot_modify_a_binding(ServiceDocumentKind kind, string source)
    {
        var binding = new ResourceBinding { Variables = new() { ["Keep"] = "original" } };
        Should.Throw<ArgumentException>(() => new ServiceStringMapDocument(kind, source).Apply(binding));
        binding.Variables.Single().Value.ShouldBe("original");
        binding.Files.ShouldBeEmpty();
    }

    [Fact]
    public void Map_edits_preserve_multiline_contents_and_case()
    {
        var draft = new ServiceDocumentDraft(ServiceDocumentKind.Files, """{"/keep":"First\r\nSecond","/edit":"old"}""");
        draft.Entries[1].Value = "new\n";
        var result = JsonNode.Parse(draft.Serialize())!;
        result["/keep"]!.GetValue<string>().ShouldBe("First\r\nSecond");
        result["/edit"]!.GetValue<string>().ShouldBe("new\n");
    }

    [Theory]
    [InlineData(ServiceDocumentKind.Files, "{\"file\":true}")]
    [InlineData(ServiceDocumentKind.NameList, "[some_function()].")]
    [InlineData(ServiceDocumentKind.KeyValueLines, "unsupported syntax\n")]
    [InlineData(ServiceDocumentKind.ObjectCollections, "{\"queues\":{\"name\":\"wrong shape\"}}")]
    public void Unsupported_source_is_not_silently_reinterpreted(ServiceDocumentKind kind, string source)
        => Should.Throw<ArgumentException>(() => new ServiceDocumentDraft(kind, source, FluxFlow.Plugins.RabbitMQ.RabbitMqAmqpSetup.Provider.StartupFiles.Last().Templates!, EnabledPlugins));
}
