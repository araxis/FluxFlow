using FluxFlow.Authoring;

namespace FluxFlow.Plugins.Hierarchies;

/// <summary>Edits a detached document. Read restores selection where possible; Save validates before returning JSON.</summary>
public interface IHierarchyDocument
{
    void Read(string json);
    string ToJson();
    string Save();
    IEnumerable<DocumentNode> Nodes { get; }
    IEnumerable<DocumentAction> Actions { get; }
    IEnumerable<DocumentField> Fields { get; }
}

public sealed record HierarchyEditor(string Title, string Group, string OpenLabel, string Subtitle, Func<IHierarchyDocument> Create);
public sealed record DocumentNode(object Key, int Depth, string Name, string Detail, bool Selected, Action Select);
public abstract record DocumentField;
public sealed record DocumentAction(string Label, Action Invoke, bool Enabled = true, string? Menu = null) : DocumentField;
public sealed record DocumentNote(string Text) : DocumentField;
public sealed record DocumentTitle(string Text, DocumentAction? Remove = null) : DocumentField;
public sealed record DocumentGroup(object Key, string Title, IReadOnlyList<DocumentField> Fields) : DocumentField;
public sealed record DocumentText(string Label, string Value, Action<string> Change, TextTools? Tools = null) : DocumentField;
public sealed record TextTools(string SearchLabel, Func<string, IEnumerable<TextSuggestion>> Search, string GenerateLabel, Func<string> Generate);
public sealed record TextSuggestion(string Value, string Label, string Detail, bool Enabled);
public sealed record DocumentChoice(string Label, string Value, Action<string> Change, IReadOnlyList<OptionChoiceModel> Choices) : DocumentField;
public sealed record DocumentFlag(string Label, bool Value, Action<bool> Change) : DocumentField;
public sealed record DocumentBinary(EncodedValue Value, Action<EncodedValue> Change, BinaryValueFormat Format) : DocumentField;
