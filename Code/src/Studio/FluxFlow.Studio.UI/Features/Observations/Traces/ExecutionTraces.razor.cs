using System.Globalization;
using System.Text.Json;
using FluxFlow.Environments;
using Microsoft.AspNetCore.Components;

namespace FluxFlow.Studio.UI.Features.Observations.Traces;

public partial class ExecutionTraces : IDisposable
{
    [Parameter, EditorRequired] public ExecutionTraceSnapshot Value { get; set; } = new([], 0);
    [Parameter] public Guid? RunId { get; set; }
    [Parameter] public string? ReadError { get; set; }
    [Parameter] public EventCallback<ExecutionTrace> OnSelectSpan { get; set; }
    [Parameter] public EventCallback<ExecutionTrace> OnShowRelatedLogs { get; set; }
    [Parameter] public ExecutionTrace? RequestedSpan { get; set; }
    [Parameter] public string SearchText { get; set; } = "";
    [Parameter] public EventCallback<string> SearchTextChanged { get; set; }
    [Parameter] public bool ErrorsOnly { get; set; }
    [Parameter] public EventCallback<bool> ErrorsOnlyChanged { get; set; }
    [Parameter] public EventCallback OnClearFilters { get; set; }
    [Parameter] public ApplicationTelemetryTarget? Application { get; set; }
    [Parameter] public EventCallback OnClearApplicationFilter { get; set; }
    private ApplicationTelemetryTarget? ActiveApplication => Application?.RunId == RunId ? Application : null;
    private ExecutionTrace? _requestedSpan;
    private Guid? _runId;
    private string? _traceId, _spanId;
    private List<IGrouping<string, ExecutionTrace>> _traces = [];
    private int _retainedTraces;
    private List<SpanRow> _rows = [];
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly List<(string TraceId, string SpanId)> _history = [];
    private readonly List<(string TraceId, string SpanId)> _forwardHistory = [];
    private long _navigationVersion;
    private DateTimeOffset _start;
    private TimeSpan _duration;
    private readonly string _viewerId = $"trace-view-{Guid.NewGuid():N}";
    private (Guid? RunId, string TraceId, string SpanId)? _pendingScroll;
    private bool _disposed;
    private sealed record SpanRow(ExecutionTrace Span, int Depth)
    {
        public DateTimeOffset BranchStart { get; set; } = Span.StartTime;
        public DateTimeOffset BranchEnd { get; set; } = Span.StartTime + Span.Duration;
        public int BranchCount { get; set; } = 1;
    }

    protected override void OnParametersSet()
    {
        if (_runId != RunId) { _runId = RunId; _traceId = _spanId = null; _collapsed.Clear(); _history.Clear(); _forwardHistory.Clear(); }
        _history.RemoveAll(item => !Value.Records.Any(span => span.TraceId == item.TraceId && span.SpanId == item.SpanId));
        _forwardHistory.RemoveAll(item => !Value.Records.Any(span => span.TraceId == item.TraceId && span.SpanId == item.SpanId));
        FilterTraces(SearchText.Trim(), ErrorsOnly, ActiveApplication);
        if (!_traces.Any(trace => trace.Key == _traceId))
        { _traceId = _traces.FirstOrDefault()?.Key; _spanId = null; _collapsed.Clear(); }
        if (!ReferenceEquals(_requestedSpan, RequestedSpan))
        {
            _requestedSpan = RequestedSpan;
            if (RequestedSpan is { } requested && _traces.Any(trace => trace.Key == requested.TraceId && trace.Any(span => span.SpanId == requested.SpanId)))
            {
                _navigationVersion++;
                if (_traceId != requested.TraceId || _spanId != requested.SpanId) RememberSelection();
                _traceId = requested.TraceId;
                _spanId = requested.SpanId;
                _collapsed.Clear();
            }
        }
        BuildRows();
    }

    private void FilterTraces(string search, bool errorsOnly, ApplicationTelemetryTarget? application)
    {
        var traces = Value.Records.GroupBy(span => span.TraceId).ToArray();
        _retainedTraces = traces.Length;
        _traces = traces
            .Where(trace => application is null || trace.Any(span =>
                span.Attributes.TryGetValue("flow.application.instance.id", out var instance) && instance.ValueKind == JsonValueKind.String &&
                instance.GetString() == application.InstanceId))
            .Where(trace => !errorsOnly || trace.Any(span => span.Status == "Error"))
            .Where(trace => search.Length == 0 || trace.Any(span => span.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || (span.Attributes.TryGetValue("flow.component.address", out var address) && address.ValueKind == JsonValueKind.String
                    && address.GetString()!.Contains(search, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(trace => trace.Min(span => span.StartTime)).ToList();
    }

    private void SelectTrace(string? id, Guid? runId, bool rememberSelection = true)
    {
        if (runId != RunId || !_traces.Any(trace => trace.Key == id)) return;
        _navigationVersion++;
        if (rememberSelection) RememberSelection();
        _traceId = id; _spanId = null; _collapsed.Clear(); BuildRows();
    }

    private void RememberSelection()
    {
        _forwardHistory.Clear();
        RememberSelection(_history, _traceId, _spanId);
    }

    private static void RememberSelection(List<(string TraceId, string SpanId)> history, string? traceId, string? spanId)
    {
        if (traceId is null || spanId is null) return;
        var current = (traceId, spanId);
        if (history.Count > 0 && history[^1] == current) return;
        if (history.Count == 100) history.RemoveAt(0);
        history.Add(current);
    }

    private (int Index, ExecutionTrace Span)? HistoryTarget(List<(string TraceId, string SpanId)> history)
    {
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var item = history[i];
            if (item == (_traceId, _spanId)) continue;
            var span = Value.Records.FirstOrDefault(span => span.TraceId == item.TraceId && span.SpanId == item.SpanId);
            if (span is not null) return (i, span);
        }
        return null;
    }

    private async Task NavigateHistory(bool forward, Guid? runId, string? traceId, string? spanId)
    {
        var history = forward ? _forwardHistory : _history;
        if (!IsCurrentSelection() || HistoryTarget(history) is not { } target) return;
        var navigationVersion = ++_navigationVersion;
        if (!_traces.Any(trace => trace.Key == target.Span.TraceId))
        {
            await OnClearFilters.InvokeAsync();
            // Clearing an empty result can select the first trace; only newer navigation should supersede this action.
            if (_disposed || runId != RunId || navigationVersion != _navigationVersion) return;
            FilterTraces("", false, null);
        }
        if (HistoryTarget(history) is not { } current || !_traces.Any(trace => trace.Key == current.Span.TraceId)) return;
        history.RemoveRange(current.Index, history.Count - current.Index);
        if (Value.Records.Any(span => span.TraceId == traceId && span.SpanId == spanId))
            RememberSelection(forward ? _history : _forwardHistory, traceId, spanId);
        if (_traceId != current.Span.TraceId) SelectTrace(current.Span.TraceId, runId, rememberSelection: false);
        await RevealSpan(current.Span, runId, rememberSelection: false);

        bool IsCurrentSelection() => !_disposed && runId == RunId && traceId == _traceId && spanId == _spanId;
    }

    internal Task SelectSpan(string? id) => SelectSpan(id, RunId, _traceId);

    private ExecutionTrace? ParentOf(ExecutionTrace span) => _rows.FirstOrDefault(row =>
        row.Span.TraceId == span.TraceId && row.Span.SpanId == span.ParentSpanId && row.Span.SpanId != span.SpanId)?.Span;

    private async Task GoToParent(Guid? runId, string traceId, string spanId)
    {
        if (_disposed || runId != RunId || traceId != _traceId || spanId != _spanId) return;
        var selected = _rows.FirstOrDefault(row => row.Span.SpanId == spanId)?.Span;
        if (selected is null || ParentOf(selected) is not { } parent) return;
        await RevealSpan(parent, runId);
    }

    private ExecutionTrace? LinkedSpan(ExecutionTraceLink link) => Value.Records.FirstOrDefault(span =>
        span.TraceId == link.TraceId && span.SpanId == link.SpanId);

    private async Task GoToLinkedSpan(ExecutionTraceLink link, Guid? runId, string traceId, string spanId)
    {
        if (!IsCurrentLink() || LinkedSpan(link) is null) return;
        if (!_traces.Any(trace => trace.Key == link.TraceId))
        {
            await OnClearFilters.InvokeAsync();
            if (!IsCurrentLink() || LinkedSpan(link) is null) return;
            // Parent parameters may not arrive until the click finishes rendering.
            FilterTraces("", false, null);
        }
        if (!IsCurrentLink() || LinkedSpan(link) is not { } target || !_traces.Any(trace => trace.Key == target.TraceId)) return;
        if (_traceId != target.TraceId) SelectTrace(target.TraceId, runId);
        await RevealSpan(target, runId);

        bool IsCurrentLink() => !_disposed && runId == RunId && traceId == _traceId && spanId == _spanId
            && _rows.Any(row => row.Span.SpanId == spanId && row.Span.Links.Any(item => item.TraceId == link.TraceId && item.SpanId == link.SpanId));
    }

    private async Task RevealSpan(ExecutionTrace span, Guid? runId, bool rememberSelection = true)
    {
        ScrollToSpan(span, runId);
        await SelectSpan(span.SpanId, runId, span.TraceId, rememberSelection);
    }

    private void RevealSelectedSpan(Guid? runId, string? traceId, string? spanId)
    {
        if (_disposed || runId != RunId || traceId != _traceId || spanId != _spanId) return;
        var selected = _rows.FirstOrDefault(row => row.Span.TraceId == traceId && row.Span.SpanId == spanId)?.Span;
        if (selected is null) return;
        ScrollToSpan(selected, runId);
        StateHasChanged();
    }

    private void ScrollToSpan(ExecutionTrace span, Guid? runId)
    {
        var index = _rows.FindIndex(row => row.Span.SpanId == span.SpanId);
        var depth = _rows[index].Depth;
        for (var i = index - 1; i >= 0 && depth > 0; i--)
        {
            if (_rows[i].Depth >= depth) continue;
            _collapsed.Remove(_rows[i].Span.SpanId);
            depth = _rows[i].Depth;
        }
        _pendingScroll = (runId, span.TraceId, span.SpanId);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var pending = _pendingScroll;
        _pendingScroll = null;
        if (_disposed || pending is not { } target || target.RunId != RunId || target.TraceId != _traceId || target.SpanId != _spanId
            || !VisibleRows().Any(row => row.Span.SpanId == target.SpanId)) return;
        try { await ScrollManager.ScrollIntoViewAsync($"#{_viewerId} .trace-selected", MudBlazor.ScrollBehavior.Auto); }
        catch (Microsoft.JSInterop.JSException) { }
        catch (OperationCanceledException) { }
    }

    public void Dispose() { _disposed = true; _pendingScroll = null; }

    private string? CopyDetails(Guid? runId, string traceId, string spanId)
    {
        if (runId != RunId || traceId != _traceId || spanId != _spanId) return null;
        var span = Value.Records.FirstOrDefault(item => item.TraceId == traceId && item.SpanId == spanId);
        return span is null ? null : JsonSerializer.Serialize(span, EnvironmentJsonContext.Default.ExecutionTrace);
    }

    internal async Task SelectSpan(string? id, Guid? runId, string? traceId, bool rememberSelection = true)
    {
        if (runId != RunId || traceId != _traceId) return;
        var span = VisibleRows().FirstOrDefault(row => row.Span.SpanId == id)?.Span;
        if (id is not null && span is null) return;
        _navigationVersion++;
        if (rememberSelection && id is not null && id != _spanId) RememberSelection();
        _spanId = id;
        StateHasChanged();
        if (span is not null)
            await OnSelectSpan.InvokeAsync(span);
    }

    internal Task ToggleSpan(string id) => ToggleSpan(id, RunId, _traceId);

    private async Task SetAllExpanded(bool expanded, Guid? runId, string? traceId)
    {
        if (runId != RunId || traceId is null || traceId != _traceId) return;
        _collapsed.Clear();
        if (!expanded)
        {
            foreach (var row in _rows.Where(HasChildren)) _collapsed.Add(row.Span.SpanId);
            if (_spanId is not null && !VisibleRows().Any(row => row.Span.SpanId == _spanId))
            {
                var ancestor = _rows.TakeWhile(row => row.Span.SpanId != _spanId).LastOrDefault(row => row.Depth == 0);
                await SelectSpan(ancestor?.Span.SpanId);
            }
        }
        StateHasChanged();
    }

    internal async Task ToggleSpan(string id, Guid? runId, string? traceId)
    {
        if (runId != RunId || traceId != _traceId || !VisibleRows().Any(row => row.Span.SpanId == id)) return;
        if (!_collapsed.Add(id)) _collapsed.Remove(id);
        if (_spanId is not null && !VisibleRows().Any(row => row.Span.SpanId == _spanId)) await SelectSpan(id);
        StateHasChanged();
    }

    private IEnumerable<SpanRow> VisibleRows()
    {
        int? hiddenBelow = null;
        foreach (var row in _rows)
        {
            if (hiddenBelow is { } depth && row.Depth > depth) continue;
            hiddenBelow = _collapsed.Contains(row.Span.SpanId) ? row.Depth : null;
            yield return row;
        }
    }

    private bool HasChildren(SpanRow row)
    {
        var index = _rows.IndexOf(row);
        return index + 1 < _rows.Count && _rows[index + 1].Depth > row.Depth;
    }

    private void BuildRows()
    {
        _rows = [];
        var spans = _traces.FirstOrDefault(trace => trace.Key == _traceId)?.OrderBy(span => span.StartTime).ThenBy(span => span.Sequence).ToArray() ?? [];
        if (spans.Length == 0) { _spanId = null; return; }
        _start = spans.Min(span => span.StartTime);
        _duration = spans.Max(span => span.StartTime + span.Duration) - _start;
        var ids = spans.Select(span => span.SpanId).ToHashSet(StringComparer.Ordinal);
        var children = spans.ToLookup(span => span.ParentSpanId ?? "", StringComparer.Ordinal);
        HashSet<string> visited = new(StringComparer.Ordinal);
        foreach (var root in spans.Where(span => span.ParentSpanId is null || !ids.Contains(span.ParentSpanId))) Visit(root, 0);
        // Keep incomplete or cyclic data visible without recursive traversal or an invented parent.
        foreach (var remaining in spans) Visit(remaining, 0);
        Stack<SpanRow> branches = new();
        foreach (var row in _rows)
        {
            while (branches.TryPeek(out var previous) && previous.Depth >= row.Depth) CompleteBranch();
            branches.Push(row);
        }
        while (branches.Count > 0) CompleteBranch();
        _collapsed.IntersectWith(ids);
        if (!VisibleRows().Any(row => row.Span.SpanId == _spanId)) _spanId = null;

        void CompleteBranch()
        {
            var child = branches.Pop();
            if (!branches.TryPeek(out var parent)) return;
            if (child.BranchStart < parent.BranchStart) parent.BranchStart = child.BranchStart;
            if (child.BranchEnd > parent.BranchEnd) parent.BranchEnd = child.BranchEnd;
            parent.BranchCount += child.BranchCount;
        }

        void Visit(ExecutionTrace root, int depth)
        {
            Stack<SpanRow> pending = new();
            pending.Push(new(root, depth));
            while (pending.TryPop(out var row))
            {
                if (!visited.Add(row.Span.SpanId)) continue;
                _rows.Add(row);
                foreach (var child in children[row.Span.SpanId].Reverse()) pending.Push(new(child, row.Depth + 1));
            }
        }
    }

    private string BarStyle(ExecutionTrace span) => BarStyle(span.StartTime, span.Duration);

    private string BarStyle(DateTimeOffset start, TimeSpan duration)
    {
        var total = (double)Math.Max(_duration.Ticks, 1);
        var left = Math.Clamp((start - _start).Ticks / total * 100, 0, 100);
        var width = Math.Clamp(duration.Ticks / total * 100, 0, 100 - left);
        return FormattableString.Invariant($"left:{left:0.####}%;width:{width:0.####}%");
    }
    private static string Milliseconds(TimeSpan time) => time.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);
    private static string ComponentName(ExecutionTrace span) => span.Attributes.TryGetValue("flow.component.address", out var address) ? address.ToString() : span.Source;
    private static string TraceLabel(IGrouping<string, ExecutionTrace> trace)
    {
        var first = trace.MinBy(span => span.StartTime)!;
        return $"{first.StartTime:HH:mm:ss.fff} · {ComponentName(first)} · {trace.Key}";
    }
}
