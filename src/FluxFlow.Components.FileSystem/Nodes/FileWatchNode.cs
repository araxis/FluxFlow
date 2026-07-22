using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;

namespace FluxFlow.Components.FileSystem.Nodes;

/// <summary>
/// Watches a directory and emits immutable <see cref="FlowValue"/> objects.
/// Runtime source failures fault <see cref="Completion"/>; lifecycle diagnostics
/// are published through <see cref="Events"/>.
/// </summary>
public sealed class FileWatchNode : IFlowSource
{
    public const string WatchStarted = FileSystemDiagnosticNames.FileWatchStarted;
    public const string WatchStopped = FileSystemDiagnosticNames.FileWatchStopped;
    public const string WatchChanged = FileSystemDiagnosticNames.FileWatchChanged;
    public const string WatchFailed = FileSystemDiagnosticNames.FileWatchFailed;

    private readonly object _stateLock = new();
    private readonly FileWatchOptions _options;
    private readonly TimeProvider _clock;
    private readonly NotifyFilters _notifyFilters;
    private readonly FileSystemSourcePipeline _pipeline;
    private FileSystemWatcher? _watcher;
    private string? _resolvedDirectory;

    public FileWatchNode(
        FileWatchOptions options,
        TimeProvider? clock = null)
    {
        var resolved = ValidateOptions(options);
        _options = resolved.Options;
        _clock = clock ?? TimeProvider.System;
        _notifyFilters = resolved.NotifyFilters;
        _pipeline = new FileSystemSourcePipeline(
            _options.BoundedCapacity,
            RunAsync);
    }

    public ISourceBlock<FlowMessage<FlowValue>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _pipeline.StartAsync(cancellationToken);

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public async ValueTask DisposeAsync()
    {
        _pipeline.Complete();
        StopWatcher();
        await _pipeline.DisposeAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        string? resolvedDirectory = null;
        try
        {
            resolvedDirectory = ResolveDirectory();
            if (!Directory.Exists(resolvedDirectory))
            {
                throw new FileSystemSourceException(
                    FileSystemErrorCodes.FileWatchDirectoryMissing,
                    $"file.watch directory '{_options.Directory}' was not found.",
                    CreateErrorContext(resolvedDirectory));
            }

            StartWatcher(resolvedDirectory);
            PublishEvent(
                WatchStarted,
                FlowEventLevel.Information,
                $"Started file watcher '{resolvedDirectory}'.",
                CreateAttributes(resolvedDirectory));

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Requested stop.
        }
        catch (Exception exception)
        {
            var failure = ClassifyFailure(exception, resolvedDirectory);
            PublishEvent(
                WatchFailed,
                FlowEventLevel.Error,
                failure.Message,
                CreateAttributes(resolvedDirectory));
            throw failure;
        }
        finally
        {
            StopWatcher();
            PublishEvent(
                WatchStopped,
                FlowEventLevel.Information,
                "Stopped file watcher.",
                CreateAttributes(resolvedDirectory));
        }
    }

    private string ResolveDirectory()
        => FileSystemPathResolver.Resolve(
            _options.Directory,
            new FileSystemPathPolicy(
                "file.watch",
                _options.BaseDirectory,
                _options.AllowAbsolutePaths,
                FileSystemErrorCodes.FileWatchInvalidDirectory,
                FileSystemErrorCodes.FileWatchAbsolutePathDenied));

    private void StartWatcher(string resolvedDirectory)
    {
        var watcher = new FileSystemWatcher(resolvedDirectory, _options.Filter)
        {
            IncludeSubdirectories = _options.IncludeSubdirectories,
            NotifyFilter = _notifyFilters,
            EnableRaisingEvents = false
        };
        try
        {
            if (_options.InternalBufferSize.HasValue)
                watcher.InternalBufferSize = _options.InternalBufferSize.Value;

            watcher.Created += OnChanged;
            watcher.Changed += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;

            lock (_stateLock)
            {
                _resolvedDirectory = resolvedDirectory;
                _watcher = watcher;
            }

            watcher.EnableRaisingEvents = true;
        }
        catch
        {
            lock (_stateLock)
            {
                if (ReferenceEquals(_watcher, watcher))
                {
                    _watcher = null;
                    _resolvedDirectory = null;
                }
            }

            ReleaseWatcher(watcher);
            throw;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs args)
        => PublishChange(new WatchSnapshot(
            _clock.GetUtcNow(),
            args.FullPath,
            _resolvedDirectory ?? Directory.GetParent(args.FullPath)?.FullName ?? string.Empty,
            args.Name,
            args.ChangeType switch
            {
                WatcherChangeTypes.Created => "Created",
                WatcherChangeTypes.Deleted => "Deleted",
                _ => "Changed"
            },
            null,
            null));

    private void OnRenamed(object sender, RenamedEventArgs args)
        => PublishChange(new WatchSnapshot(
            _clock.GetUtcNow(),
            args.FullPath,
            _resolvedDirectory ?? Directory.GetParent(args.FullPath)?.FullName ?? string.Empty,
            args.Name,
            "Renamed",
            args.OldFullPath,
            args.OldName));

    private void OnError(object sender, ErrorEventArgs args)
    {
        if (_pipeline.IsStopping)
            return;

        var exception = args.GetException();
        var failure = new FileSystemSourceException(
            FileSystemErrorCodes.FileWatchFailed,
            $"file.watch failed: {exception.Message}",
            CreateErrorContext(_resolvedDirectory),
            exception);
        PublishEvent(
            WatchFailed,
            FlowEventLevel.Error,
            failure.Message,
            CreateAttributes(_resolvedDirectory));
        _pipeline.Fault(failure);
    }

    private void PublishChange(WatchSnapshot value)
    {
        if (_pipeline.IsStopping)
            return;

        if (!_pipeline.TryEmit(FlowMessage.Create(ToFlowValue(value))))
        {
            _pipeline.Fault(new FileSystemSourceException(
                FileSystemErrorCodes.FileWatchFailed,
                "file.watch output is not accepting events.",
                CreateErrorContext(value.Directory)));
            return;
        }

        PublishEvent(
            WatchChanged,
            FlowEventLevel.Information,
            $"Observed file change '{value.Path}'.",
            CreateAttributes(value));
    }

    private static FlowValue ToFlowValue(WatchSnapshot value)
        => FlowValue.FromObject(new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["timestamp"] = FlowValue.From(value.Timestamp),
            ["path"] = FlowValue.From(value.Path),
            ["directory"] = FlowValue.From(value.Directory),
            ["name"] = OptionalValue(value.Name),
            ["changeType"] = FlowValue.From(value.ChangeType),
            ["oldPath"] = OptionalValue(value.OldPath),
            ["oldName"] = OptionalValue(value.OldName)
        });

    private FileSystemSourceException ClassifyFailure(
        Exception exception,
        string? resolvedDirectory)
        => exception switch
        {
            FileSystemSourceException source => source,
            FileSystemPathResolutionException path => new FileSystemSourceException(
                path.Code,
                path.Message,
                CreateErrorContext(resolvedDirectory),
                path),
            UnauthorizedAccessException access => new FileSystemSourceException(
                FileSystemErrorCodes.FileWatchStartupFailed,
                $"file.watch startup failed: {access.Message}",
                CreateErrorContext(resolvedDirectory),
                access),
            IOException io => new FileSystemSourceException(
                FileSystemErrorCodes.FileWatchStartupFailed,
                $"file.watch startup failed: {io.Message}",
                CreateErrorContext(resolvedDirectory),
                io),
            _ => new FileSystemSourceException(
                FileSystemErrorCodes.FileWatchStartupFailed,
                $"file.watch startup failed: {exception.Message}",
                CreateErrorContext(resolvedDirectory),
                exception)
        };

    private static ResolvedFileWatchOptions ValidateOptions(FileWatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BoundedCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "boundedCapacity must be positive.");
        if (string.IsNullOrWhiteSpace(options.Directory))
            throw new ArgumentException("directory cannot be empty.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Filter))
            throw new ArgumentException("filter cannot be empty.", nameof(options));
        if (options.InternalBufferSize is < 4096 or > 65536)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "internalBufferSize must be between 4096 and 65536 bytes when set.");
        return new ResolvedFileWatchOptions(options, FileWatchNotifyFilters.Resolve(options));
    }

    private void PublishEvent(
        string name,
        FlowEventLevel level,
        string message,
        IReadOnlyDictionary<string, object?> attributes)
        => _pipeline.PublishEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = name,
            Level = level,
            Message = message,
            Attributes = attributes
        });

    private Dictionary<string, object?> CreateAttributes(string? resolvedDirectory)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["directory"] = _options.Directory,
            ["filter"] = _options.Filter,
            ["includeSubdirectories"] = _options.IncludeSubdirectories,
            ["notifyFilters"] = _notifyFilters.ToString()
        };
        if (!string.IsNullOrWhiteSpace(resolvedDirectory))
            attributes["resolvedDirectory"] = resolvedDirectory;
        if (!string.IsNullOrWhiteSpace(_options.BaseDirectory))
            attributes["baseDirectory"] = _options.BaseDirectory;
        return attributes;
    }

    private Dictionary<string, object?> CreateAttributes(WatchSnapshot value)
    {
        var attributes = CreateAttributes(value.Directory);
        attributes["path"] = value.Path;
        attributes["changeType"] = value.ChangeType;
        if (!string.IsNullOrWhiteSpace(value.Name))
            attributes["name"] = value.Name;
        if (!string.IsNullOrWhiteSpace(value.OldPath))
            attributes["oldPath"] = value.OldPath;
        if (!string.IsNullOrWhiteSpace(value.OldName))
            attributes["oldName"] = value.OldName;
        return attributes;
    }

    private string CreateErrorContext(string? resolvedDirectory)
    {
        var values = new List<string>
        {
            $"directory={_options.Directory}",
            $"filter={_options.Filter}",
            $"includeSubdirectories={_options.IncludeSubdirectories}"
        };
        resolvedDirectory ??= _resolvedDirectory;
        if (!string.IsNullOrWhiteSpace(resolvedDirectory))
            values.Add($"resolvedDirectory={resolvedDirectory}");
        if (!string.IsNullOrWhiteSpace(_options.BaseDirectory))
            values.Add($"baseDirectory={_options.BaseDirectory}");
        return string.Join("; ", values);
    }

    private void StopWatcher()
    {
        FileSystemWatcher? watcher;
        lock (_stateLock)
        {
            watcher = _watcher;
            _watcher = null;
            _resolvedDirectory = null;
        }

        if (watcher is null)
            return;

        ReleaseWatcher(watcher);
    }

    private void ReleaseWatcher(FileSystemWatcher watcher)
    {
        try
        {
            watcher.EnableRaisingEvents = false;
        }
        catch (ObjectDisposedException)
        {
            // Handler detachment remains safe after an external disposal race.
        }

        watcher.Created -= OnChanged;
        watcher.Changed -= OnChanged;
        watcher.Deleted -= OnChanged;
        watcher.Renamed -= OnRenamed;
        watcher.Error -= OnError;
        watcher.Dispose();
    }

    private static FlowValue OptionalValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? FlowValue.Null : FlowValue.From(value.Trim());

    private sealed record ResolvedFileWatchOptions(
        FileWatchOptions Options,
        NotifyFilters NotifyFilters);

    private sealed record WatchSnapshot(
        DateTimeOffset Timestamp,
        string Path,
        string Directory,
        string? Name,
        string ChangeType,
        string? OldPath,
        string? OldName);
}
