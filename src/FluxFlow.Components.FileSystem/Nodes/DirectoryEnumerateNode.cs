using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.FileSystem.Nodes;

/// <summary>
/// Enumerates a directory as immutable <see cref="DirectoryEntry"/> records. Runtime
/// source failures fault <see cref="Completion"/>; lifecycle diagnostics are
/// published through <see cref="Events"/>.
/// </summary>
public sealed class DirectoryEnumerateNode : IFlowSource
{
    public const string EnumerateStarted = FileSystemDiagnosticNames.DirectoryEnumerateStarted;
    public const string EnumerateEntry = FileSystemDiagnosticNames.DirectoryEnumerateEntry;
    public const string EnumerateCompleted = FileSystemDiagnosticNames.DirectoryEnumerateCompleted;
    public const string EnumerateFailed = FileSystemDiagnosticNames.DirectoryEnumerateFailed;

    private readonly DirectoryEnumerateOptions _options;
    private readonly TimeProvider _clock;
    private readonly FileSystemSourcePipeline<DirectoryEntry> _pipeline;

    public DirectoryEnumerateNode(
        DirectoryEnumerateOptions options,
        TimeProvider? clock = null)
    {
        _options = ValidateOptions(options);
        _clock = clock ?? TimeProvider.System;
        _pipeline = new FileSystemSourcePipeline<DirectoryEntry>(
            _options.BoundedCapacity,
            RunAsync);
    }

    public ISourceBlock<FlowMessage<DirectoryEntry>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public Task StartAsync(CancellationToken cancellationToken = default)
        => _pipeline.StartAsync(cancellationToken);

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        string? resolvedDirectory = null;
        long emitted = 0;
        try
        {
            resolvedDirectory = ResolveDirectory();
            if (!Directory.Exists(resolvedDirectory))
            {
                throw new FileSystemSourceException(
                    FileSystemErrorCodes.DirectoryEnumerateDirectoryMissing,
                    $"directory.enumerate directory '{_options.Directory}' was not found.",
                    CreateErrorContext(resolvedDirectory));
            }

            PublishEvent(
                EnumerateStarted,
                FlowEventLevel.Information,
                $"Started directory enumeration '{resolvedDirectory}'.",
                CreateAttributes(resolvedDirectory));

            foreach (var entry in Enumerate(resolvedDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_options.MaxEntries.HasValue && emitted >= _options.MaxEntries.Value)
                    break;

                await _pipeline.EmitAsync(
                        FlowMessage.Create(entry),
                        cancellationToken)
                    .ConfigureAwait(false);

                emitted++;
                PublishEvent(
                    EnumerateEntry,
                    FlowEventLevel.Information,
                    $"Enumerated '{entry.Path}'.",
                    CreateAttributes(entry, emitted));
            }

            PublishEvent(
                EnumerateCompleted,
                FlowEventLevel.Information,
                $"Completed directory enumeration '{resolvedDirectory}'.",
                CreateAttributes(resolvedDirectory, emitted));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishEvent(
                EnumerateCompleted,
                FlowEventLevel.Information,
                resolvedDirectory is null
                    ? "Stopped directory enumeration."
                    : $"Stopped directory enumeration '{resolvedDirectory}'.",
                CreateAttributes(resolvedDirectory, emitted));
            throw;
        }
        catch (Exception exception)
        {
            var failure = ClassifyFailure(exception, resolvedDirectory);
            PublishEvent(
                EnumerateFailed,
                FlowEventLevel.Error,
                failure.Message,
                CreateAttributes(resolvedDirectory, emitted));
            throw failure;
        }
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

    private IEnumerable<DirectoryEntry> Enumerate(string resolvedDirectory)
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
            foreach (var path in Directory.EnumerateDirectories(
                         resolvedDirectory,
                         _options.Filter,
                         enumerationOptions))
            {
                var directory = new DirectoryInfo(path);
                yield return new DirectoryEntry(
                    _clock.GetUtcNow(),
                    directory.FullName,
                    resolvedDirectory,
                    directory.Name,
                    "Directory",
                    null,
                    Timestamp(directory.CreationTimeUtc),
                    Timestamp(directory.LastWriteTimeUtc),
                    directory.Attributes);
            }
        }

        if (_options.IncludeFiles)
        {
            foreach (var path in Directory.EnumerateFiles(
                         resolvedDirectory,
                         _options.Filter,
                         enumerationOptions))
            {
                var file = new FileInfo(path);
                yield return new DirectoryEntry(
                    _clock.GetUtcNow(),
                    file.FullName,
                    resolvedDirectory,
                    file.Name,
                    "File",
                    file.Length,
                    Timestamp(file.CreationTimeUtc),
                    Timestamp(file.LastWriteTimeUtc),
                    file.Attributes);
            }
        }
    }

    private static DateTimeOffset? Timestamp(DateTime value)
        => value == DateTime.MinValue
            ? null
            : new DateTimeOffset(value, TimeSpan.Zero);

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
                FileSystemErrorCodes.DirectoryEnumerateAccessDenied,
                $"directory.enumerate access was denied for '{resolvedDirectory}'.",
                CreateErrorContext(resolvedDirectory),
                access),
            IOException io => new FileSystemSourceException(
                FileSystemErrorCodes.DirectoryEnumerateIoFailed,
                $"directory.enumerate failed for '{resolvedDirectory}': {io.Message}",
                CreateErrorContext(resolvedDirectory),
                io),
            _ => new FileSystemSourceException(
                FileSystemErrorCodes.DirectoryEnumerateIoFailed,
                $"directory.enumerate failed: {exception.Message}",
                CreateErrorContext(resolvedDirectory),
                exception)
        };

    private static DirectoryEnumerateOptions ValidateOptions(DirectoryEnumerateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.BoundedCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "boundedCapacity must be positive.");
        if (string.IsNullOrWhiteSpace(options.Directory))
            throw new ArgumentException("directory cannot be empty.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Filter))
            throw new ArgumentException("filter cannot be empty.", nameof(options));
        if (!options.IncludeFiles && !options.IncludeDirectories)
            throw new ArgumentException("includeFiles or includeDirectories must be enabled.", nameof(options));
        if (options.MaxEntries is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "maxEntries must be positive when set.");
        return options;
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
            attributes["resolvedDirectory"] = resolvedDirectory;
        if (!string.IsNullOrWhiteSpace(_options.BaseDirectory))
            attributes["baseDirectory"] = _options.BaseDirectory;
        if (_options.MaxEntries.HasValue)
            attributes["maxEntries"] = _options.MaxEntries.Value;
        if (emitted.HasValue)
            attributes["entries"] = emitted.Value;
        return attributes;
    }

    private Dictionary<string, object?> CreateAttributes(
        DirectoryEntry entry,
        long emitted)
    {
        var attributes = CreateAttributes(entry.Directory, emitted);
        attributes["path"] = entry.Path;
        attributes["name"] = entry.Name;
        attributes["entryType"] = entry.EntryType;
        if (entry.Length.HasValue)
            attributes["length"] = entry.Length.Value;
        return attributes;
    }

    private string CreateErrorContext(string? resolvedDirectory)
    {
        var values = new List<string>
        {
            $"directory={_options.Directory}",
            $"filter={_options.Filter}",
            $"includeSubdirectories={_options.IncludeSubdirectories}",
            $"includeFiles={_options.IncludeFiles}",
            $"includeDirectories={_options.IncludeDirectories}"
        };
        if (!string.IsNullOrWhiteSpace(resolvedDirectory))
            values.Add($"resolvedDirectory={resolvedDirectory}");
        if (!string.IsNullOrWhiteSpace(_options.BaseDirectory))
            values.Add($"baseDirectory={_options.BaseDirectory}");
        if (_options.MaxEntries.HasValue)
            values.Add($"maxEntries={_options.MaxEntries.Value}");
        return string.Join("; ", values);
    }

}
