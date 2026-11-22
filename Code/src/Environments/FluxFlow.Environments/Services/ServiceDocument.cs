using System.Text.Json.Nodes;

namespace FluxFlow.Environments;

/// <summary>A provider-owned JSON document of editable items inside a service profile.</summary>
public sealed record ServiceDocument(string Label, string Item, string Items, string Description,
    Func<ResourceBinding, string> Read, Func<ResourceBinding, string, ResourceBinding> Write,
    Action<string> Validate, Func<JsonObject> Create, Func<JsonObject, IServiceDocumentItem> Open);

public interface IServiceDocumentItem
{
    string Title { get; }
    string Summary { get; }
    bool CanEdit { get; }
    IReadOnlyList<DocumentSection> Sections { get; }
    JsonObject Apply();
}

public sealed record DocumentSection(string Title, IReadOnlyList<DocumentField> Fields,
    DocumentPairs? Pairs = null, DocumentSource? Source = null);
public abstract record DocumentField(string Label);
public sealed record DocumentText(string Label, Func<string> Read, Action<string> Write, string? Placeholder = null) : DocumentField(Label);
public sealed record DocumentFlag(string Label, Func<bool> Read, Action<bool> Write) : DocumentField(Label);
public sealed record DocumentReadOnly(string Label, string Value) : DocumentField(Label);
public sealed record DocumentChoice(string Label, Func<string> Read, Action<string> Write, IReadOnlyList<string> Choices) : DocumentField(Label);
public sealed record DocumentInteger(string Label, Func<int> Read, Action<int> Write, int Min, int Max) : DocumentField(Label);
public sealed record DocumentSource(string Label, Func<string> Language, Func<string> Read, Action<string> Write);
public sealed record DocumentPairs(string Label, string Item, IList<DocumentPair> Entries);
public sealed class DocumentPair
{
    public string? OriginalValue { get; internal init; }
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}
