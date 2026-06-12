using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Components.FileSystem.Timing;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.FileSystem.Nodes;

public sealed class DirectoryEnumerateNode : SourceFlowNode<DirectoryEnumerateEntry>, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly DirectoryEnumerateOptions _options;
    private readonly IFileSystemClock _clock;
    private CancellationTokenSource? _enumerationCancellation;
    private Task? _enumerationTask;
    private string? _resolvedDirectory;
    private bool _started;
    private bool _disposed;

    private DirectoryEnumerateNode(
        DirectoryEnumerateOptions options,
        IFileSystemClock clock)
        : base(new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity })
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Directory enumerate bounded capacity must be greater than zero.");
        }
    }

    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
        => Create(context, new FileSystemComponentOptions());

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        FileSystemComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = FileSystemOptionsReader.ReadDirectoryEnumerateOptions(context.Definition);
        var node = new DirectoryEnumerateNode(options, componentOptions.Clock);

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
                throw new InvalidOperationException("directory.enumerate node has already started.");
            }

            _started = true;
        }

        try
        {
            var resolvedDirectory = ResolveDirectory();
            if (!System.IO.Directory.Exists(resolvedDirectory))
            {
                throw new DirectoryEnumerateNodeException(
                    FileSystemErrorCodes.DirectoryEnumerateDirectoryMissing,
                    $"directory.enumerate directory '{_options.Directory}' was not found.");
            }

            var enumerationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            lock (_stateLock)
            {
                _resolvedDirectory = resolvedDirectory;
                _enumerationCancellation = enumerationCancellation;
                TryEmitDiagnostic(
                    FileSystemDiagnosticNames.DirectoryEnumerateStarted,
                    message: $"Started directory enumeration '{resolvedDirectory}'.",
                    attributes: CreateAttributes(resolvedDirectory));
                _enumerationTask = Task.Run(
                    () => RunEnumerationAsync(resolvedDirectory, enumerationCancellation.Token),
                    CancellationToken.None);
            }

            return Task.CompletedTask;
        }
        catch (FileSystemPathResolutionException exception)
        {
            ReportEnumerateError(exception.Code, exception.Message, exception);
            TryEmitDiagnostic(
                FileSystemDiagnosticNames.DirectoryEnumerateFailed,
                FlowDiagnosticLevel.Error,
                exception.Message,
                exception,
                CreateAttributes());
            throw new InvalidOperationException("directory.enumerate failed to start.", exception);
        }
        catch (DirectoryEnumerateNodeException exception)
        {
            ReportEnumerateError(exception.Code, exception.Message, exception);
            TryEmitDiagnostic(
                FileSystemDiagnosticNames.DirectoryEnumerateFailed,
                FlowDiagnosticLevel.Error,
                exception.Message,
                exception,
                CreateAttributes());
            throw new InvalidOperationException("directory.enumerate failed to start.", exception);
        }
        catch (Exception exception)
        {
            ReportEnumerateError(
                FileSystemErrorCodes.DirectoryEnumerateFailed,
                $"directory.enumerate startup failed: {exception.Message}",
                exception);
            TryEmitDiagnostic(
                FileSystemDiagnosticNames.DirectoryEnumerateFailed,
                FlowDiagnosticLevel.Error,
                "directory.enumerate startup failed.",
                exception,
                CreateAttributes());
            throw new InvalidOperationException("directory.enumerate failed to start.", exception);
        }
    }

    public override void Complete()
    {
        _enumerationCancellation?.Cancel();
        CompleteOutput();
    }

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _enumerationCancellation?.Cancel();
        base.Fault(exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Complete();

        if (_enumerationTask is not null)
        {
            try
            {
                await _enumerationTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _enumerationCancellation?.Dispose();
    }

    private string ResolveDirectory()
        => FileSystemPathResolver.Resolve(
            _options.Directory,
            new FileSystemPathPolicy(
                "directory.enumerate",
                _options.BaseDirectory,
                _options.AllowAbsolutePaths,
                FileSystemErrorCodes.DirectoryEnumerateInvalidDirectory,
                FileSystemErrorCodes.DirectoryEnumerateAbsolutePathDenied));

    private async Task RunEnumerationAsync(
        string resolvedDirectory,
        CancellationToken cancellationToken)
    {
        long emitted = 0;
        try
        {
            foreach (var entry in Enumerate(resolvedDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_options.MaxEntries.HasValue && emitted >= _options.MaxEntries.Value)
                {
                    break;
                }

                if (!await SendOutputAsync(entry, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                emitted++;

                TryEmitDiagnostic(
                    FileSystemDiagnosticNames.DirectoryEnumerateEntry,
                    message: $"Enumerated '{entry.Path}'.",
                    attributes: CreateAttributes(entry, emitted));
            }

            TryEmitDiagnostic(
                FileSystemDiagnosticNames.DirectoryEnumerateCompleted,
                message: $"Completed directory enumeration '{resolvedDirectory}'.",
                attributes: CreateAttributes(resolvedDirectory, emitted));
            CompleteOutput();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryEmitDiagnostic(
                FileSystemDiagnosticNames.DirectoryEnumerateCompleted,
                message: $"Stopped directory enumeration '{resolvedDirectory}'.",
                attributes: CreateAttributes(resolvedDirectory, emitted));
            CompleteOutput();
        }
        catch (UnauthorizedAccessException exception)
        {
            FailEnumeration(
                FileSystemErrorCodes.DirectoryEnumerateAccessDenied,
                $"directory.enumerate access was denied for '{resolvedDirectory}'.",
                exception,
                resolvedDirectory,
                emitted);
        }
        catch (IOException exception)
        {
            FailEnumeration(
                FileSystemErrorCodes.DirectoryEnumerateIoFailed,
                $"directory.enumerate failed for '{resolvedDirectory}': {exception.Message}",
                exception,
                resolvedDirectory,
                emitted);
        }
        catch (Exception exception)
        {
            FailEnumeration(
                FileSystemErrorCodes.DirectoryEnumerateFailed,
                $"directory.enumerate failed: {exception.Message}",
                exception,
                resolvedDirectory,
                emitted);
        }
    }

    private IEnumerable<DirectoryEnumerateEntry> Enumerate(string resolvedDirectory)
    {
        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = _options.IncludeSubdirectories,
            IgnoreInaccessible = false,
            MatchType = MatchType.Win32,
            AttributesToSkip = _options.IncludeSubdirectories
                ? FileAttributes.ReparsePoint
                : FileAttributes.None
        };

        if (_options.IncludeDirectories)
        {
            foreach (var directory in System.IO.Directory.EnumerateDirectories(
                         resolvedDirectory,
                         _options.Filter,
                         enumerationOptions))
            {
                yield return CreateDirectoryEntry(new DirectoryInfo(directory), resolvedDirectory);
            }
        }

        if (_options.IncludeFiles)
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(
                         resolvedDirectory,
                         _options.Filter,
                         enumerationOptions))
            {
                yield return CreateFileEntry(new FileInfo(file), resolvedDirectory);
            }
        }
    }

    private DirectoryEnumerateEntry CreateDirectoryEntry(
        DirectoryInfo directory,
        string resolvedDirectory)
        => new()
        {
            EnumeratedAt = _clock.UtcNow,
            Path = directory.FullName,
            Directory = resolvedDirectory,
            Name = directory.Name,
            EntryType = DirectoryEntryType.Directory,
            CreatedAt = directory.CreationTimeUtc == DateTime.MinValue
                ? null
                : new DateTimeOffset(directory.CreationTimeUtc, TimeSpan.Zero),
            LastModifiedAt = directory.LastWriteTimeUtc == DateTime.MinValue
                ? null
                : new DateTimeOffset(directory.LastWriteTimeUtc, TimeSpan.Zero),
            Attributes = directory.Attributes
        };

    private DirectoryEnumerateEntry CreateFileEntry(
        FileInfo file,
        string resolvedDirectory)
        => new()
        {
            EnumeratedAt = _clock.UtcNow,
            Path = file.FullName,
            Directory = resolvedDirectory,
            Name = file.Name,
            EntryType = DirectoryEntryType.File,
            Length = file.Length,
            CreatedAt = file.CreationTimeUtc == DateTime.MinValue
                ? null
                : new DateTimeOffset(file.CreationTimeUtc, TimeSpan.Zero),
            LastModifiedAt = file.LastWriteTimeUtc == DateTime.MinValue
                ? null
                : new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero),
            Attributes = file.Attributes
        };

    private void FailEnumeration(
        int code,
        string message,
        Exception exception,
        string resolvedDirectory,
        long emitted)
    {
        ReportEnumerateError(code, message, exception);
        TryEmitDiagnostic(
            FileSystemDiagnosticNames.DirectoryEnumerateFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            CreateAttributes(resolvedDirectory, emitted));
        base.Fault(exception);
    }

    private void ReportEnumerateError(
        int code,
        string message,
        Exception? exception = null)
        => TryReportError(code, message, exception, CreateErrorContext());

    private Dictionary<string, object?> CreateAttributes(
        string? resolvedDirectory = null,
        long? emitted = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["directory"] = _options.Directory,
            ["filter"] = _options.Filter,
            ["includeSubdirectories"] = _options.IncludeSubdirectories,
            ["includeFiles"] = _options.IncludeFiles,
            ["includeDirectories"] = _options.IncludeDirectories
        };

        if (!string.IsNullOrWhiteSpace(resolvedDirectory))
        {
            attributes["resolvedDirectory"] = resolvedDirectory;
        }

        if (!string.IsNullOrWhiteSpace(_options.BaseDirectory))
        {
            attributes["baseDirectory"] = _options.BaseDirectory;
        }

        if (_options.MaxEntries.HasValue)
        {
            attributes["maxEntries"] = _options.MaxEntries.Value;
        }

        if (emitted.HasValue)
        {
            attributes["entries"] = emitted.Value;
        }

        return attributes;
    }

    private Dictionary<string, object?> CreateAttributes(
        DirectoryEnumerateEntry entry,
        long emitted)
    {
        var attributes = CreateAttributes(entry.Directory, emitted);
        attributes["path"] = entry.Path;
        attributes["name"] = entry.Name;
        attributes["entryType"] = entry.EntryType.ToString();
        if (entry.Length.HasValue)
        {
            attributes["length"] = entry.Length.Value;
        }

        return attributes;
    }

    private string CreateErrorContext()
    {
        var values = new List<string>
        {
            $"directory={_options.Directory}",
            $"filter={_options.Filter}",
            $"includeSubdirectories={_options.IncludeSubdirectories}",
            $"includeFiles={_options.IncludeFiles}",
            $"includeDirectories={_options.IncludeDirectories}"
        };

        if (!string.IsNullOrWhiteSpace(_resolvedDirectory))
        {
            values.Add($"resolvedDirectory={_resolvedDirectory}");
        }

        if (!string.IsNullOrWhiteSpace(_options.BaseDirectory))
        {
            values.Add($"baseDirectory={_options.BaseDirectory}");
        }

        if (_options.MaxEntries.HasValue)
        {
            values.Add($"maxEntries={_options.MaxEntries.Value}");
        }

        return string.Join("; ", values);
    }

    private sealed class DirectoryEnumerateNodeException : Exception
    {
        public DirectoryEnumerateNodeException(int code, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
        }

        public int Code { get; }
    }
}
