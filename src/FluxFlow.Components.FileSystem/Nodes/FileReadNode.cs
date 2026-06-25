using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Nodes;
using System.Text;

namespace FluxFlow.Components.FileSystem.Nodes;

/// <summary>
/// A standalone file-read node. Post a <c>FlowMessage&lt;FileReadRequest&gt;</c> to
/// <c>Input</c>; the node resolves the path under its options, reads the file as text
/// or bytes, and broadcasts a <c>FlowMessage&lt;FileReadResult&gt;</c> on <c>Output</c>
/// carrying the same correlation id (a note on <c>Events</c>). Path/encoding/size and
/// IO failures surface on <c>Errors</c> with the request's correlation id, and the node
/// keeps processing later messages. Works with nothing but
/// <c>new FileReadNode(options)</c> — no engine.
/// </summary>
public sealed class FileReadNode : FlowNode<FileReadRequest, FileReadResult>
{
    public const string ReadSucceeded = FileSystemDiagnosticNames.FileReadSucceeded;
    public const string ReadFailed = FileSystemDiagnosticNames.FileReadFailed;

    private readonly FileReadOptions _options;
    private readonly TimeProvider _clock;

    public FileReadNode(
        FileReadOptions? options = null,
        TimeProvider? clock = null)
        : this(ResolveOptions(options), clock, validated: true)
    {
    }

    private FileReadNode(
        FileReadOptions options,
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

    private static FileReadOptions ResolveOptions(FileReadOptions? options)
    {
        var resolved = options ?? new FileReadOptions();
        if (resolved.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "file.read option 'boundedCapacity' must be greater than zero.");
        }

        if (resolved.MaxBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "file.read option 'maxBytes' must be greater than zero when set.");
        }

        ValidateDefaultEncoding(resolved.DefaultEncoding);
        return resolved;
    }

    protected override async Task ProcessAsync(FlowMessage<FileReadRequest> message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var request = message.Payload;

        ResolvedRead resolved;
        try
        {
            resolved = ResolveRead(request);
        }
        catch (FileReadNodeException exception)
        {
            ReportReadError(exception.Code, exception.Message, message, exception.InnerException);
            return;
        }
        catch (FileSystemPathResolutionException exception)
        {
            ReportReadError(exception.Code, exception.Message, message, exception.InnerException);
            return;
        }

        try
        {
            var fileInfo = new FileInfo(resolved.Path);
            if (!fileInfo.Exists)
            {
                ReportReadError(
                    FileSystemErrorCodes.FileReadNotFound,
                    $"file.read could not find '{request.Path}'.",
                    message,
                    resolvedPath: resolved.Path);
                return;
            }

            if (ExceedsMaxBytes(fileInfo.Length))
            {
                ReportReadError(
                    FileSystemErrorCodes.FileReadTooLarge,
                    $"file.read file '{request.Path}' exceeds maxBytes.",
                    message,
                    resolvedPath: resolved.Path,
                    bytesRead: fileInfo.Length);
                return;
            }

            var bytes = await File.ReadAllBytesAsync(resolved.Path, Stopping).ConfigureAwait(false);
            if (ExceedsMaxBytes(bytes.LongLength))
            {
                ReportReadError(
                    FileSystemErrorCodes.FileReadTooLarge,
                    $"file.read file '{request.Path}' exceeds maxBytes.",
                    message,
                    resolvedPath: resolved.Path,
                    bytesRead: bytes.LongLength);
                return;
            }

            var result = CreateResult(request, resolved, bytes);

            // Carry the correlation id forward onto the result.
            Emit(message.With(result));
            EmitEvent(new FlowEvent
            {
                Timestamp = _clock.GetUtcNow(),
                CorrelationId = message.CorrelationId,
                Name = ReadSucceeded,
                Level = FlowEventLevel.Information,
                Message = $"Read file '{resolved.Path}'.",
                Attributes = CreateAttributes(request, resolved.Path, result.BytesRead, result.Encoding)
            });
        }
        catch (FileReadNodeException exception)
        {
            ReportReadError(
                exception.Code,
                exception.Message,
                message,
                exception.InnerException,
                resolved.Path);
        }
        catch (UnauthorizedAccessException exception)
        {
            ReportReadError(
                FileSystemErrorCodes.FileReadAccessDenied,
                $"file.read access was denied for '{request.Path}'.",
                message,
                exception,
                resolved.Path);
        }
        catch (ArgumentException exception)
        {
            ReportReadError(
                FileSystemErrorCodes.FileReadInvalidPath,
                $"file.read request path is invalid: {exception.Message}",
                message,
                exception,
                resolved.Path);
        }
        catch (NotSupportedException exception)
        {
            ReportReadError(
                FileSystemErrorCodes.FileReadInvalidPath,
                $"file.read request path is invalid: {exception.Message}",
                message,
                exception,
                resolved.Path);
        }
        catch (PathTooLongException exception)
        {
            ReportReadError(
                FileSystemErrorCodes.FileReadInvalidPath,
                $"file.read request path is too long: {exception.Message}",
                message,
                exception,
                resolved.Path);
        }
        catch (FileNotFoundException exception)
        {
            ReportReadError(
                FileSystemErrorCodes.FileReadNotFound,
                $"file.read could not find '{request.Path}'.",
                message,
                exception,
                resolved.Path);
        }
        catch (DirectoryNotFoundException exception)
        {
            ReportReadError(
                FileSystemErrorCodes.FileReadNotFound,
                $"file.read could not find '{request.Path}'.",
                message,
                exception,
                resolved.Path);
        }
        catch (IOException exception)
        {
            ReportReadError(
                FileSystemErrorCodes.FileReadIoFailed,
                $"file.read failed for '{request.Path}': {exception.Message}",
                message,
                exception,
                resolved.Path);
        }
    }

    private ResolvedRead ResolveRead(FileReadRequest request)
    {
        if (!Enum.IsDefined(request.ReadAs))
        {
            throw new FileReadNodeException(
                FileSystemErrorCodes.FileReadUnsupportedMode,
                $"file.read request uses unsupported read mode '{request.ReadAs}'.");
        }

        var path = FileSystemPathResolver.Resolve(
            request.Path,
            new FileSystemPathPolicy(
                "file.read",
                _options.BaseDirectory,
                _options.AllowAbsolutePaths,
                FileSystemErrorCodes.FileReadInvalidPath,
                FileSystemErrorCodes.FileReadAbsolutePathDenied));

        if (request.ReadAs == FileReadMode.Bytes)
        {
            return new ResolvedRead(path, Encoding: null, EncodingName: null);
        }

        var encodingName = ResolveEncodingName(request);
        var encoding = ResolveEncoding(encodingName);

        return new ResolvedRead(path, encoding, encodingName);
    }

    private FileReadResult CreateResult(FileReadRequest request, ResolvedRead resolved, byte[] bytes)
    {
        var readAt = _clock.GetUtcNow();
        if (request.ReadAs == FileReadMode.Bytes)
        {
            return new FileReadResult
            {
                Path = resolved.Path,
                Bytes = bytes,
                BytesRead = bytes.LongLength,
                ReadAs = request.ReadAs,
                ReadAt = readAt
            };
        }

        return new FileReadResult
        {
            Path = resolved.Path,
            Content = resolved.Encoding!.GetString(bytes),
            Encoding = resolved.EncodingName,
            BytesRead = bytes.LongLength,
            ReadAs = request.ReadAs,
            ReadAt = readAt
        };
    }

    private static Encoding ResolveEncoding(string encodingName)
    {
        try
        {
            return Encoding.GetEncoding(encodingName);
        }
        catch (ArgumentException exception)
        {
            throw new FileReadNodeException(
                FileSystemErrorCodes.FileReadUnsupportedEncoding,
                $"file.read request uses unsupported encoding '{encodingName}'.",
                exception);
        }
    }

    private string ResolveEncodingName(FileReadRequest request)
        => string.IsNullOrWhiteSpace(request.Encoding)
            ? _options.DefaultEncoding
            : request.Encoding;

    private bool ExceedsMaxBytes(long byteCount)
        => _options.MaxBytes.HasValue && byteCount > _options.MaxBytes.Value;

    private static void ValidateDefaultEncoding(string defaultEncoding)
    {
        if (string.IsNullOrWhiteSpace(defaultEncoding))
        {
            throw new ArgumentException(
                "file.read option 'defaultEncoding' cannot be empty.",
                nameof(defaultEncoding));
        }

        try
        {
            Encoding.GetEncoding(defaultEncoding);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException(
                "file.read option 'defaultEncoding' is not supported.",
                nameof(defaultEncoding),
                exception);
        }
    }

    private void ReportReadError(
        int code,
        string message,
        FlowMessage<FileReadRequest> source,
        Exception? exception = null,
        string? resolvedPath = null,
        long? bytesRead = null)
    {
        var request = source.Payload;
        EmitError(new FlowError
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Code = code,
            Message = message,
            Context = CreateErrorContext(request, resolvedPath),
            Exception = exception
        });
        EmitEvent(new FlowEvent
        {
            Timestamp = _clock.GetUtcNow(),
            CorrelationId = source.CorrelationId,
            Name = ReadFailed,
            Level = FlowEventLevel.Error,
            Message = message,
            Attributes = CreateAttributes(request, resolvedPath, bytesRead)
        });
    }

    private Dictionary<string, object?> CreateAttributes(
        FileReadRequest request,
        string? resolvedPath = null,
        long? bytesRead = null,
        string? encoding = null)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["path"] = request.Path,
            ["readAs"] = request.ReadAs.ToString()
        };

        if (!string.IsNullOrWhiteSpace(resolvedPath))
        {
            attributes["resolvedPath"] = resolvedPath;
        }

        if (bytesRead.HasValue)
        {
            attributes["bytesRead"] = bytesRead.Value;
        }

        if (!string.IsNullOrWhiteSpace(encoding))
        {
            attributes["encoding"] = encoding;
        }

        if (_options.MaxBytes.HasValue)
        {
            attributes["maxBytes"] = _options.MaxBytes.Value;
        }

        if (!string.IsNullOrWhiteSpace(_options.BaseDirectory))
        {
            attributes["baseDirectory"] = _options.BaseDirectory;
        }

        return attributes;
    }

    private string CreateErrorContext(FileReadRequest request, string? resolvedPath = null)
    {
        var values = new List<string>
        {
            $"path={request.Path}",
            $"readAs={request.ReadAs}"
        };

        if (!string.IsNullOrWhiteSpace(resolvedPath))
        {
            values.Add($"resolvedPath={resolvedPath}");
        }

        if (!string.IsNullOrWhiteSpace(_options.BaseDirectory))
        {
            values.Add($"baseDirectory={_options.BaseDirectory}");
        }

        if (_options.MaxBytes.HasValue)
        {
            values.Add($"maxBytes={_options.MaxBytes.Value}");
        }

        return string.Join("; ", values);
    }

    private sealed record ResolvedRead(string Path, Encoding? Encoding, string? EncodingName);

    private sealed class FileReadNodeException : Exception
    {
        public FileReadNodeException(int code, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
        }

        public int Code { get; }
    }
}
