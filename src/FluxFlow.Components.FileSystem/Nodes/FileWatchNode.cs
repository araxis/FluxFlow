using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Components.FileSystem.Timing;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.FileSystem.Nodes;

public sealed class FileWatchNode : SourceFlowNode<FileWatchEvent>, IFlowEventSource, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly FileWatchOptions _options;
    private readonly IFileSystemClock _clock;
    private readonly NotifyFilters _notifyFilters;
    private readonly BroadcastBlock<FlowEvent> _events = new(static flowEvent => flowEvent);
    private FileSystemWatcher? _watcher;
    private string? _resolvedDirectory;
    private bool _started;
    private bool _disposed;

    private FileWatchNode(
        FileWatchOptions options,
        IFileSystemClock clock)
        : base(new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity })
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "File watch bounded capacity must be greater than zero.");
        }

        _notifyFilters = FileSystemOptionsReader.ResolveNotifyFilters(options);
    }

    public ISourceBlock<FlowEvent> Events => _events;

    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
        => Create(context, new FileSystemComponentOptions());

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        FileSystemComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = FileSystemOptionsReader.ReadFileWatchOptions(context.Definition);
        var node = new FileWatchNode(options, componentOptions.Clock);

        return context.CreateNode(node)
            .Output(FileSystemComponentPorts.Output, node.Output)
            .Build();
    }

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_stateLock)
        {
            if (_started)
            {
                throw new InvalidOperationException("file.watch node has already started.");
            }

            _started = true;
        }

        FileSystemWatcher? watcher = null;
        try
        {
            var resolvedDirectory = ResolveDirectory();
            if (!System.IO.Directory.Exists(resolvedDirectory))
            {
                throw new FileWatchNodeException(
                    FileSystemErrorCodes.FileWatchDirectoryMissing,
                    $"file.watch directory '{_options.Directory}' was not found.");
            }

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

            try
            {
                watcher.EnableRaisingEvents = true;
            }
            catch
            {
                lock (_stateLock)
                {
                    _watcher = null;
                }

                throw;
            }

            watcher = null;

            TryEmitDiagnostic(
                FileSystemDiagnosticNames.FileWatchStarted,
                message: $"Started file watcher '{resolvedDirectory}'.",
                attributes: CreateAttributes(resolvedDirectory));
            return Task.CompletedTask;
        }
        catch (FileSystemPathResolutionException exception)
        {
            watcher?.Dispose();
            ReportWatchError(exception.Code, exception.Message, exception);
            TryEmitDiagnostic(
                FileSystemDiagnosticNames.FileWatchFailed,
                FlowDiagnosticLevel.Error,
                exception.Message,
                exception,
                CreateAttributes());
            throw new InvalidOperationException("file.watch failed to start.", exception);
        }
        catch (FileWatchNodeException exception)
        {
            watcher?.Dispose();
            ReportWatchError(exception.Code, exception.Message, exception);
            TryEmitDiagnostic(
                FileSystemDiagnosticNames.FileWatchFailed,
                FlowDiagnosticLevel.Error,
                exception.Message,
                exception,
                CreateAttributes());
            throw new InvalidOperationException("file.watch failed to start.", exception);
        }
        catch (Exception exception)
        {
            watcher?.Dispose();
            ReportWatchError(
                FileSystemErrorCodes.FileWatchStartupFailed,
                $"file.watch startup failed: {exception.Message}",
                exception);
            TryEmitDiagnostic(
                FileSystemDiagnosticNames.FileWatchFailed,
                FlowDiagnosticLevel.Error,
                "file.watch startup failed.",
                exception,
                CreateAttributes());
            throw new InvalidOperationException("file.watch failed to start.", exception);
        }
    }

    public override void Complete()
    {
        StopWatcher();
        CompleteOutput();
    }

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        StopWatcher();
        base.Fault(exception);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        Complete();
        return ValueTask.CompletedTask;
    }

    protected override void OnNodeCompleted()
    {
        TryEmitDiagnostic(
            FileSystemDiagnosticNames.FileWatchStopped,
            message: "Stopped file watcher.",
            attributes: CreateAttributes(_resolvedDirectory));
        _events.Complete();
        base.OnNodeCompleted();
    }

    protected override void OnNodeFaulted(Exception exception)
    {
        ((IDataflowBlock)_events).Fault(exception);
        base.OnNodeFaulted(exception);
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
            Timestamp = _clock.UtcNow,
            Path = args.FullPath,
            Directory = _resolvedDirectory ?? System.IO.Directory.GetParent(args.FullPath)?.FullName ?? string.Empty,
            Name = args.Name,
            ChangeType = changeType
        });
    }

    private void OnRenamed(object sender, RenamedEventArgs args)
        => PublishChange(new FileWatchEvent
        {
            Timestamp = _clock.UtcNow,
            Path = args.FullPath,
            Directory = _resolvedDirectory ?? System.IO.Directory.GetParent(args.FullPath)?.FullName ?? string.Empty,
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
            exception);
        TryEmitDiagnostic(
            FileSystemDiagnosticNames.FileWatchFailed,
            FlowDiagnosticLevel.Error,
            "file.watch failed.",
            exception,
            CreateAttributes(_resolvedDirectory));
    }

    private void PublishChange(FileWatchEvent watchEvent)
    {
        if (!PostOutput(watchEvent))
        {
            if (Output.Completion.IsCompleted)
            {
                return;
            }

            ReportWatchError(
                FileSystemErrorCodes.FileWatchOutputFull,
                $"file.watch output queue is full for '{watchEvent.Path}'.");
            TryEmitDiagnostic(
                FileSystemDiagnosticNames.FileWatchDropped,
                FlowDiagnosticLevel.Warning,
                "file.watch dropped an event because the output queue is full.",
                attributes: CreateAttributes(watchEvent));
            return;
        }

        TryEmitDiagnostic(
            FileSystemDiagnosticNames.FileWatchChanged,
            message: $"Observed file change '{watchEvent.Path}'.",
            attributes: CreateAttributes(watchEvent));
        EmitChangeEvent(watchEvent);
    }

    private void EmitChangeEvent(FileWatchEvent watchEvent)
    {
        var attributes = new Dictionary<string, string>
        {
            ["changeType"] = watchEvent.ChangeType.ToString(),
            ["path"] = watchEvent.Path
        };

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

        _events.Post(new FlowEvent
        {
            Timestamp = watchEvent.Timestamp,
            Type = FileSystemEventNames.FileWatchChanged,
            Source = Id.ToString(),
            SourceNodeId = Id,
            Subject = watchEvent.Path,
            Channel = FileSystemEventNames.FileWatchChanged,
            Attributes = attributes
        });
    }

    private void ReportWatchError(
        int code,
        string message,
        Exception? exception = null)
        => TryReportError(code, message, exception, CreateErrorContext());

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

    private string CreateErrorContext()
    {
        var values = new List<string>
        {
            $"directory={_options.Directory}",
            $"filter={_options.Filter}",
            $"includeSubdirectories={_options.IncludeSubdirectories}"
        };

        if (!string.IsNullOrWhiteSpace(_resolvedDirectory))
        {
            values.Add($"resolvedDirectory={_resolvedDirectory}");
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

    private sealed class FileWatchNodeException : Exception
    {
        public FileWatchNodeException(int code, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
        }

        public int Code { get; }
    }
}
