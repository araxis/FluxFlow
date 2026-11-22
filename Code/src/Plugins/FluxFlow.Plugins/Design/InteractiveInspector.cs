using System.Text.Json;
using FluxFlow.Authoring;

namespace FluxFlow.Plugins;

/// <summary>Component-owned inspection of live resources. Draft and preview state belong to one Inspector instance.</summary>
public interface IInteractiveInspector : IDisposable
{
    void Refresh(IDictionary<string, JsonElement> settings, long revision);
    IEnumerable<InspectorSection> Sections { get; }
    InspectorResource Resource { get; }
    InspectorPicker Picker { get; }
    InspectorPreview Preview { get; }
}

public sealed record InspectorSection(string Name, IEnumerable<InspectorRow> Rows, bool HasPicker = false);
public abstract record InspectorRow;
public sealed record InspectorOption(OptionEditorModel Option, string? Revision = null) : InspectorRow;
public sealed record InspectorSummary(string Label, string Value) : InspectorRow;
public sealed record InspectorMode(string Label, bool Value, string FalseLabel, string TrueLabel, Action<bool> Change) : InspectorRow;
public sealed record InspectorExample(string Title, string Text) : InspectorRow;
public sealed record InspectorResource(string Label, string Type, string Value, string EmptyChoice, string MissingHint,
    string EmptyHint, Action<string> Change) : InspectorRow;
public sealed record InspectorBytes(EncodedValue Value, BinaryValueFormat Format, Action<EncodedValue> Change) : InspectorRow;

public sealed record InspectorPicker(string Title, Func<ExplorerContext, ISelectionExplorer> Open, Action<object> Apply);
public sealed record ExplorerContext(Func<string, IReadOnlyList<string>> Endpoints, Func<string, string?> Name, Func<bool> Current);
public sealed record InspectorPreview(string Title, string Description, IReadOnlyList<KeyValuePair<string, string>> Fields,
    string Command, bool Enabled, Func<Func<bool>, Task> Run, string? Hint, string? Status, string? Error,
    byte[]? Bytes, string? ResultCaption, Action? Cancel, string CancelLabel);
