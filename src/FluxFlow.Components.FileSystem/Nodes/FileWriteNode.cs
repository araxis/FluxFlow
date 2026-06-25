using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Nodes;
using System.Text;

namespace FluxFlow.Components.FileSystem.Nodes;

/// <summary>
/// A standalone file-write node. Post a <c>FlowMessage&lt;FileWriteRequest&gt;</c> to
/// <c>Input</c>; the node resolves the path under its options, writes the request's
/// content or bytes (overwrite / append / create-new), and broadcasts a
/// <c>FlowMessage&lt;FileWriteResult&gt;</c> on <c>Output</c> carrying the same
/// correlation id (a note on <c>Events</c>). Path/encoding/content and IO failures
/// surface on <c>Errors</c> with the request's correlation id, and the node keeps
/// processing later messages. Works with nothing but <c>new FileWriteNode(options)</c>
/// — no engine.
/// </summary>
public sealed class FileWriteNode : FlowNode<FileWriteRequest, FileWriteResult>
{
    public const string WriteSucceeded = FileSystemDiagnosticNames.FileWriteSucceeded;
    public const string WriteFailed = FileSystemDiagnosticNames.FileWriteFailed;

    private readonly FileWriteOptions _options;
    private readonly TimeProvider _clock;

    public FileWriteNode(
        FileWriteOptions? options = null,
        TimeProvider? clock = null)
        : this(ResolveOptions(options), clock, validated: true)
    {
    }

    private FileWriteNode(
        FileWriteOptions options,
        TimeProvider? clock,
        bool validated)
        : base(new FlowNodeOptions
        {
            InputCapacity = options.BoundedCapacity,
            MaxDegreeOfParallelism = 1
        })
    {
        _options = options;
        _clock = clock ?? TimeProvider.System;
    }

    private static FileWriteOptions ResolveOptions(FileWriteOptions? options)
    {
        var resolved = options ?? new FileWriteOptions();
        if (resolved.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "file.write option 'boundedCapacity' must be greater than zero.");
        }

        ValidateDefaultEncoding(resolved.DefaultEncoding);
        return resolved;
    }

    protected override async Task ProcessAsync(FlowMessage<FileWriteRequest> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var request = message.Payload;

        ResolvedWrite resolved;
        try
        {
            resolved = ResolveWrite(request);
        }
        catch (FileWriteNodeException exception)
        {
            ReportWriteError(exception.Code, exception.Message, message, exception.InnerException);
            return;
        }
        catch (FileSystemPathResolutionException exception)
        {
            ReportWriteError(exception.Code, exception.Message, message, exception.InnerException);
            return;
        }

        try
        {
            if (request.CreateDirectories &&
                Path.GetDirectoryName(resolved.Path) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            switch (request.Mode)
            {
                case FileWriteMode.Overwrite:
                    await File.WriteAllBytesAsync(resolved.Path, resolved.Bytes, Stopping).ConfigureAwait(false);
                    break;
                case FileWriteMode.Append:
                    await using (var stream = new FileStream(
                                     resolved.Path,
                                     FileMode.Append,
                                     FileAccess.Write,
                                     FileShare.Read,
                                     bufferSize: 4096,
                                     useAsync: true))
                    {
                        await stream.WriteAsync(resolved.Bytes, Stopping).ConfigureAwait(false);
                    }

                    break;
                case FileWriteMode.CreateNew:
                    await using (var stream = new FileStream(
                                     resolved.Path,
                                     FileMode.CreateNew,
                                     FileAccess.Write,
                                     FileShare.Read,
                                     bufferSize: 4096,
                                     useAsync: true))
                    {
                        await stream.WriteAsync(resolved.Bytes, Stopping).ConfigureAwait(false);
                    }

                    break;
                default:
                    ReportWriteError(
                        FileSystemErrorCodes.FileWriteUnsupportedMode,
                        $"file.write request uses unsupported mode '{request.Mode}'.",
                        message);
                    return;
            }

            var result = new FileWriteResult
            {
                Path = resolved.Path,
                BytesWritten = resolved.Bytes.Length,
                Mode = request.Mode,
                WrittenAt = _clock.GetUtcNow()
            };

            // Carry the correlation id forward onto the result.
            Emit(message.With(result));
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = WriteSucceeded,
                Level = FlowEventLevel.Information,
                Message = $"Wrote file '{resolved.Path}'.",
                Attributes = CreateAttributes(request, resolved.Path, resolved.Bytes.Length)
            });
        }
        catch (UnauthorizedAccessException exception)
        {
            ReportWriteError(
                FileSystemErrorCodes.FileWriteAccessDenied,
                $"file.write access was denied for '{request.Path}'.",
                message,
                exception);
        }
        catch (ArgumentException exception)
        {
            ReportWriteError(
                FileSystemErrorCodes.FileWriteInvalidPath,
                $"file.write request path is invalid: {exception.Message}",
                message,
                exception);
        }
        catch (NotSupportedException exception)
        {
            ReportWriteError(
                FileSystemErrorCodes.FileWriteInvalidPath,
                $"file.write request path is invalid: {exception.Message}",
                message,
                exception);
        }
        catch (PathTooLongException exception)
        {
            ReportWriteError(
                FileSystemErrorCodes.FileWriteInvalidPath,
                $"file.write request path is too long: {exception.Message}",
                message,
                exception);
        }
        catch (IOException exception)
        {
            ReportWriteError(
                FileSystemErrorCodes.FileWriteIoFailed,
                $"file.write failed for '{request.Path}': {exception.Message}",
                message,
                exception);
        }
    }

    private ResolvedWrite ResolveWrite(FileWriteRequest request)
    {
        if (!Enum.IsDefined(request.Mode))
        {
            throw new FileWriteNodeException(
                FileSystemErrorCodes.FileWriteUnsupportedMode,
                $"file.write request uses unsupported mode '{request.Mode}'.");
        }

        var path = ResolvePath(request.Path);
        var bytes = ResolveBytes(request);

        return new ResolvedWrite(path, bytes);
    }

    private string ResolvePath(string requestPath)
        => FileSystemPathResolver.Resolve(
            requestPath,
            new FileSystemPathPolicy(
                "file.write",
                _options.BaseDirectory,
                _options.AllowAbsolutePaths,
                FileSystemErrorCodes.FileWriteInvalidPath,
                FileSystemErrorCodes.FileWriteAbsolutePathDenied));

    private byte[] ResolveBytes(FileWriteRequest request)
    {
        if (request.Bytes is { } bytes)
        {
            return bytes;
        }

        if (request.Content is null)
        {
            throw new FileWriteNodeException(
                FileSystemErrorCodes.FileWriteContentMissing,
                "file.write request requires Content or Bytes.");
        }

        try
        {
            var encodingName = string.IsNullOrWhiteSpace(request.Encoding)
                ? _options.DefaultEncoding
                : request.Encoding;
            var encoding = Encoding.GetEncoding(encodingName);
            return encoding.GetBytes(request.Content);
        }
        catch (ArgumentException exception)
        {
            throw new FileWriteNodeException(
                FileSystemErrorCodes.FileWriteUnsupportedEncoding,
                $"file.write request uses unsupported encoding '{ResolveEncodingName(request)}'.",
                exception);
        }
    }

    private string ResolveEncodingName(FileWriteRequest request)
        => string.IsNullOrWhiteSpace(request.Encoding)
            ? _options.DefaultEncoding
            : request.Encoding;

    private static void ValidateDefaultEncoding(string defaultEncoding)
    {
        if (string.IsNullOrWhiteSpace(defaultEncoding))
        {
            throw new ArgumentException(
                "file.write option 'defaultEncoding' cannot be empty.",
                nameof(defaultEncoding));
        }

        try
        {
            Encoding.GetEncoding(defaultEncoding);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                "file.write option 'defaultEncoding' is not supported.",
                nameof(defaultEncoding),
                exception);
        }
    }

    private void ReportWriteError(
        int code,
        string message,
        FlowMessage<FileWriteRequest> source,
        Exception? exception = null)
    {
        var request = source.Payload;
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = code,
            Message = message,
            Context = CreateErrorContext(request),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = WriteFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = CreateAttributes(request)
        });
    }

    private Dictionary<string, object?> CreateAttributes(
        FileWriteRequest request,
        string? resolvedPath = null,
        int? bytesWritten = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["path"] = request.Path,
            ["mode"] = request.Mode.ToString(),
            ["createDirectories"] = request.CreateDirectories
        };

        if (!string.IsNullOrWhiteSpace(resolvedPath))
        {
            attributes["resolvedPath"] = resolvedPath;
        }

        if (bytesWritten.HasValue)
        {
            attributes["bytesWritten"] = bytesWritten.Value;
        }

        if (!string.IsNullOrWhiteSpace(_options.BaseDirectory))
        {
            attributes["baseDirectory"] = _options.BaseDirectory;
        }

        return attributes;
    }

    private string CreateErrorContext(FileWriteRequest request)
    {
        var values = new List<string>
        {
            $"path={request.Path}",
            $"mode={request.Mode}"
        };

        if (!string.IsNullOrWhiteSpace(_options.BaseDirectory))
        {
            values.Add($"baseDirectory={_options.BaseDirectory}");
        }

        return string.Join("; ", values);
    }

    private sealed record ResolvedWrite(string Path, byte[] Bytes);

    private sealed class FileWriteNodeException : Exception
    {
        public FileWriteNodeException(int code, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
        }

        public int Code { get; }
    }
}
