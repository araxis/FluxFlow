using FluxFlow.Authoring;

namespace FluxFlow.Plugins;

/// <summary>A draft selection from a hierarchy. Nothing is applied to the project until the dialog is confirmed.</summary>
public interface ISelectionExplorer : IDisposable
{
    string TreeLabel { get; }
    string EmptyTitle { get; }
    string EmptyHint { get; }
    string SelectionSummary { get; }
    ExplorerField Source { get; }
    string? ManualEntryLabel { get; }
    IReadOnlyList<ExplorerField> Inputs { get; }
    IReadOnlyList<ExplorerCommand> Commands { get; }
    ExplorerDetails Details { get; }
    long Version { get; }
    bool Busy { get; }
    string? Error { get; }
    string? Status { get; }
    ExplorerSelection? Selection { get; }
    IReadOnlyList<ExplorerItem<ExplorerSelection>> Roots();
    Task Select(ExplorerSelection? item, long version);
    Task Expand(ExplorerSelection item, bool expanded, long version);
    bool CanConfirm { get; }
    object? Confirm();
}

public abstract record ExplorerSelection;
public sealed record ExplorerField(string Label, string Value, Func<string, Task> Change)
{
    public IReadOnlyList<OptionChoiceModel> Choices { get; init; } = [];
    public string? Placeholder { get; init; }
    public string? ResourceType { get; init; }
    public string? NoConnectionsHint { get; init; }
    public Func<Task>? FinishEntry { get; init; }
}
public enum ExplorerCommandKind { Search, Refresh, Cancel }
public sealed record ExplorerCommand(string Label, ExplorerCommandKind Kind, Func<Task> Invoke, bool Enabled, bool Compact = false);
public sealed record ExplorerDetails(string Title, string Hint, IReadOnlyList<KeyValuePair<string, string>> Fields);
