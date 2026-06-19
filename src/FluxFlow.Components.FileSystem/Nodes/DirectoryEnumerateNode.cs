using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Nodes;

namespace FluxFlow.Components.FileSystem.Nodes;

/// <summary>
/// A standalone directory-enumeration source. Once <c>StartAsync</c> is called the node
/// resolves its configured directory, enumerates matching files and/or directories, and
/// broadcasts each one as a <c>FlowMessage&lt;DirectoryEnumerateEntry&gt;</c> on
/// <c>Output</c> (each minting a fresh correlation id), then completes. Diagnostics go on
/// <c>Events</c>; resolution / access / IO failures go on <c>Errors</c>. Works with
/// nothing but <c>new DirectoryEnumerateNode(options)</c> — no engine.
/// </summary>
public sealed class DirectoryEnumerateNode : FlowSource<DirectoryEnumerateEntry>
{
    public const string EnumerateStarted = FileSystemDiagnosticNames.DirectoryEnumerateStarted;
    public const string EnumerateEntry = FileSystemDiagnosticNames.DirectoryEnumerateEntry;
    public const string EnumerateCompleted = FileSystemDiagnosticNames.DirectoryEnumerateCompleted;
    public const string EnumerateFailed = FileSystemDiagnosticNames.DirectoryEnumerateFailed;

    private readonly DirectoryEnumerateOptions _options;
    private readonly TimeProvider _clock;

    public DirectoryEnumerateNode(
        DirectoryEnumerateOptions options,
        TimeProvider? clock = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? TimeProvider.System;
        ValidateOptions(_options);
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
            ReportEnumerateError(exception.Code, exception.Message, exception);
            return;
        }

        if (!Directory.Exists(resolvedDirectory))
        {
            ReportEnumerateError(
                FileSystemErrorCodes.DirectoryEnumerateDirectoryMissing,
                $"directory.enumerate directory '{_options.Directory}' was not found.");
            return;
        }

        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = EnumerateStarted,
            Level = FlowEventLevel.Information,
            Message = $"Started directory enumeration '{resolvedDirectory}'.",
            Attributes = CreateAttributes(resolvedDirectory)
        });

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

                Emit(FlowMessage.Create(entry));
                emitted++;

                EmitEvent(new FlowEvent
                {
                    Timestamp = _clock.GetUtcNow(),
                    Name = EnumerateEntry,
                    Level = FlowEventLevel.Information,
                    Message = $"Enumerated '{entry.Path}'.",
                    Attributes = CreateAttributes(entry, emitted)
                });
            }

            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                Name = EnumerateCompleted,
                Level = FlowEventLevel.Information,
                Message = $"Completed directory enumeration '{resolvedDirectory}'.",
                Attributes = CreateAttributes(resolvedDirectory, emitted)
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                Name = EnumerateCompleted,
                Level = FlowEventLevel.Information,
                Message = $"Stopped directory enumeration '{resolvedDirectory}'.",
                Attributes = CreateAttributes(resolvedDirectory, emitted)
            });
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

        await Task.CompletedTask.ConfigureAwait(false);
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
            foreach (var directory in Directory.EnumerateDirectories(
                         resolvedDirectory,
                         _options.Filter,
                         enumerationOptions))
            {
                yield return CreateDirectoryEntry(new DirectoryInfo(directory), resolvedDirectory);
            }
        }

        if (_options.IncludeFiles)
        {
            foreach (var file in Directory.EnumerateFiles(
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
            EnumeratedAt = _clock.GetUtcNow(),
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
            EnumeratedAt = _clock.GetUtcNow(),
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
        ReportEnumerateError(code, message, exception, resolvedDirectory);
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            Name = EnumerateFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = CreateAttributes(resolvedDirectory, emitted)
        });
    }

    private void ReportEnumerateError(
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

    private static void ValidateOptions(DirectoryEnumerateOptions options)
    {
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Directory enumerate bounded capacity must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.Directory))
        {
            throw new ArgumentException(
                "directory.enumerate option 'directory' cannot be empty.",
                nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.Filter))
        {
            throw new ArgumentException(
                "directory.enumerate option 'filter' cannot be empty.",
                nameof(options));
        }

        if (!options.IncludeFiles && !options.IncludeDirectories)
        {
            throw new ArgumentException(
                "directory.enumerate requires includeFiles or includeDirectories.",
                nameof(options));
        }

        if (options.MaxEntries is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "directory.enumerate option 'maxEntries' must be greater than zero when set.");
        }
    }

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

    private string CreateErrorContext(string? resolvedDirectory = null)
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
        {
            values.Add($"resolvedDirectory={resolvedDirectory}");
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
}
