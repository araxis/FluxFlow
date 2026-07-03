using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.FileSystem.Nodes;

/// <summary>
/// A standalone file-watch source. Once <c>StartAsync</c> is called the node arms a
/// <see cref="FileSystemWatcher"/> over its resolved directory and broadcasts each change
/// as a <c>FlowMessage&lt;FileWatchEvent&gt;</c> on <c>Output</c> (each minting a fresh
/// correlation id), plus a diagnostic on <c>Events</c>. It runs until <c>Complete</c>/dispose
/// stops it; resolution / watcher failures go on <c>Errors</c>. Works with nothing but
/// <c>new FileWatchNode(options)</c> — no engine.
/// </summary>
public sealed class FileWatchNode : FlowSource<FileWatchEvent>
{
    public const string WatchStarted = FileSystemDiagnosticNames.FileWatchStarted;
    public const string WatchStopped = FileSystemDiagnosticNames.FileWatchStopped;
    public const string WatchChanged = FileSystemDiagnosticNames.FileWatchChanged;
    public const string WatchFailed = FileSystemDiagnosticNames.FileWatchFailed;

    private readonly object _stateLock = new();
    private readonly FileWatchOptions _options;
    private readonly TimeProvider _clock;
    private readonly NotifyFilters _notifyFilters;
    private FileSystemWatcher? _watcher;
    private string? _resolvedDirectory;

    public FileWatchNode(
        FileWatchOptions options,
        TimeProvider? clock = null)
        : this(ResolveOptions(options), clock)
    {
    }

    private FileWatchNode(
        ResolvedFileWatchOptions resolved,
        TimeProvider? clock)
        : base(new FlowSourceOptions { OutputCapacity = resolved.Options.BoundedCapacity })
    {
        _options = resolved.Options;
        _clock = clock ?? TimeProvider.System;
        _notifyFilters = resolved.NotifyFilters;
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        string resolvedDirectory;
        try
        {
            resolvedDirectory = ResolveDirectory();
        }
        catch (FileSystemPathResolutionException exception)
        {
            ReportWatchError(exception.Code, exception.Message, exception);
            return;
        }

        if (!Directory.Exists(resolvedDirectory))
        {
            ReportWatchError(
                FileSystemErrorCodes.FileWatchDirectoryMissing,
                $"file.watch directory '{_options.Directory}' was not found.");
            return;
        }

        FileSystemWatcher watcher;
        try
        {
            watcher = new FileSystemWatcher(resolvedDirectory, _options.Filter)
            {
                IncludeSubdirectories = _options.IncludeSubdirectories,
                NotifyFilter = _notifyFilters,
                EnableRaisingEvents = false
            };
            if (_options.InternalBufferSize.HasValue)
            {
                watcher.InternalBufferSize = _options.InternalBufferSize.Value;
            }

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
        catch (Exception exception)
        {
            StopWatcher();
            ReportWatchError(
                FileSystemErrorCodes.FileWatchStartupFailed,
                $"file.watch startup failed: {exception.Message}",
                exception);
            return;
        }

        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = WatchStarted,
            Level = FlowEventLevel.Information,
            Message = $"Started file watcher '{resolvedDirectory}'.",
            Attributes = CreateAttributes(resolvedDirectory)
        });

        try
        {
            // Event-driven: the watcher emits via its callbacks; await the stop signal.
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Requested stop.
        }
        finally
        {
            StopWatcher();
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                Name = WatchStopped,
                Level = FlowEventLevel.Information,
                Message = "Stopped file watcher.",
                Attributes = CreateAttributes(resolvedDirectory)
            });
        }
    }

    protected override ValueTask OnDisposeAsync()
    {
        StopWatcher();
        return ValueTask.CompletedTask;
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

    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        var changeType = args.ChangeType switch
        {
            WatcherChangeTypes.Created => FileWatchChangeType.Created,
            WatcherChangeTypes.Changed => FileWatchChangeType.Changed,
            WatcherChangeTypes.Deleted => FileWatchChangeType.Deleted,
            _ => FileWatchChangeType.Changed
        };

        PublishChange(new FileWatchEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Path = args.FullPath,
            Directory = _resolvedDirectory ?? Directory.GetParent(args.FullPath)?.FullName ?? string.Empty,
            Name = args.Name,
            ChangeType = changeType
        });
    }

    private void OnRenamed(object sender, RenamedEventArgs args)
        => PublishChange(new FileWatchEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Path = args.FullPath,
            Directory = _resolvedDirectory ?? Directory.GetParent(args.FullPath)?.FullName ?? string.Empty,
            Name = args.Name,
            ChangeType = FileWatchChangeType.Renamed,
            OldPath = args.OldFullPath,
            OldName = args.OldName
        });

    private void OnError(object sender, ErrorEventArgs args)
    {
        var exception = args.GetException();
        ReportWatchError(
            FileSystemErrorCodes.FileWatchFailed,
            $"file.watch failed: {exception.Message}",
            exception,
            _resolvedDirectory);
    }

    private void PublishChange(FileWatchEvent watchEvent)
    {
        // Broadcast output; carries a fresh correlation id for this change.
        if (!Emit(FlowMessage.Create(watchEvent)))
        {
            ReportWatchError(
                FileSystemErrorCodes.FileWatchFailed,
                "file.watch output is not accepting events.",
                resolvedDirectory: watchEvent.Directory);
            return;
        }

        EmitEvent(new FlowEvent
        {
            Timestamp = watchEvent.Timestamp,
            Name = WatchChanged,
            Level = FlowEventLevel.Information,
            Message = $"Observed file change '{watchEvent.Path}'.",
            Attributes = CreateAttributes(watchEvent)
        });
    }

    private void ReportWatchError(
        int code,
        string message,
        Exception? exception = null,
        string? resolvedDirectory = null)
        => EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            Code = code,
            Message = message,
            Context = CreateErrorContext(resolvedDirectory),
            Exception = exception
        });

    private static ResolvedFileWatchOptions ResolveOptions(FileWatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "file.watch option 'boundedCapacity' must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.Directory))
        {
            throw new ArgumentException(
                "file.watch option 'directory' cannot be empty.",
                nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Filter))
        {
            throw new ArgumentException(
                "file.watch option 'filter' cannot be empty.",
                nameof(options));
        }

        if (options.InternalBufferSize is < 4096 or > 65536)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "file.watch option 'internalBufferSize' must be between 4096 and 65536 bytes when set.");
        }

        return new ResolvedFileWatchOptions(options, FileWatchNotifyFilters.Resolve(options));
    }

    private sealed record ResolvedFileWatchOptions(
        FileWatchOptions Options,
        NotifyFilters NotifyFilters);

    private Dictionary<string, object?> CreateAttributes(string? resolvedDirectory = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["directory"] = _options.Directory,
            ["filter"] = _options.Filter,
            ["includeSubdirectories"] = _options.IncludeSubdirectories,
            ["notifyFilters"] = _notifyFilters.ToString()
        };

        if (!string.IsNullOrWhiteSpace(resolvedDirectory))
        {
            attributes["resolvedDirectory"] = resolvedDirectory;
        }

        if (!string.IsNullOrWhiteSpace(_options.BaseDirectory))
        {
            attributes["baseDirectory"] = _options.BaseDirectory;
        }

        return attributes;
    }

    private Dictionary<string, object?> CreateAttributes(FileWatchEvent watchEvent)
    {
        var attributes = CreateAttributes(watchEvent.Directory);
        attributes["path"] = watchEvent.Path;
        attributes["changeType"] = watchEvent.ChangeType.ToString();

        if (!string.IsNullOrWhiteSpace(watchEvent.Name))
        {
            attributes["name"] = watchEvent.Name;
        }

        if (!string.IsNullOrWhiteSpace(watchEvent.OldPath))
        {
            attributes["oldPath"] = watchEvent.OldPath;
        }

        if (!string.IsNullOrWhiteSpace(watchEvent.OldName))
        {
            attributes["oldName"] = watchEvent.OldName;
        }

        return attributes;
    }

    private string CreateErrorContext(string? resolvedDirectory = null)
    {
        var values = new List<string>
        {
            $"directory={_options.Directory}",
            $"filter={_options.Filter}",
            $"includeSubdirectories={_options.IncludeSubdirectories}"
        };

        resolvedDirectory ??= _resolvedDirectory;
        if (!string.IsNullOrWhiteSpace(resolvedDirectory))
        {
            values.Add($"resolvedDirectory={resolvedDirectory}");
        }

        if (!string.IsNullOrWhiteSpace(_options.BaseDirectory))
        {
            values.Add($"baseDirectory={_options.BaseDirectory}");
        }

        return string.Join("; ", values);
    }

    private void StopWatcher()
    {
        FileSystemWatcher? watcher;
        lock (_stateLock)
        {
            watcher = _watcher;
            _watcher = null;
        }

        if (watcher is null)
        {
            return;
        }

        watcher.EnableRaisingEvents = false;
        watcher.Created -= OnChanged;
        watcher.Changed -= OnChanged;
        watcher.Deleted -= OnChanged;
        watcher.Renamed -= OnRenamed;
        watcher.Error -= OnError;
        watcher.Dispose();
    }
}
