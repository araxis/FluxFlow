using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using System.Text;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.FileSystem.Nodes;

public sealed class FileReadNode : FlowNodeBase
{
    private readonly FileReadOptions _options;
    private readonly ActionBlock<FileReadRequest> _input;
    private readonly BufferBlock<FileReadResult> _result;

    private FileReadNode(FileReadOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "File read bounded capacity must be greater than zero.");
        }

        _input = new ActionBlock<FileReadRequest>(
            ReadAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.BoundedCapacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _result = new BufferBlock<FileReadResult>(
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        CompleteWhen(_input.Completion);
    }

    public ITargetBlock<FileReadRequest> Input => _input;

    public ISourceBlock<FileReadResult> Result => _result;

    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = FileSystemOptionsReader.ReadFileReadOptions(context.Definition);
        var node = new FileReadNode(options);

        return context.CreateNode(node)
            .Input(FileSystemComponentPorts.Input, node.Input)
            .Output(FileSystemComponentPorts.Result, node.Result)
            .Build();
    }

    public override void Complete()
        => _input.Complete();

    public override void Fault(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            FaultNode(exception);
        }
        finally
        {
            ((IDataflowBlock)_input).Fault(exception);
            ((IDataflowBlock)_result).Fault(exception);
        }
    }

    protected override void OnNodeCompleted()
    {
        _result.Complete();
        base.OnNodeCompleted();
    }

    protected override void OnNodeFaulted(Exception exception)
    {
        ((IDataflowBlock)_result).Fault(exception);
        base.OnNodeFaulted(exception);
    }

    private async Task ReadAsync(FileReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ResolvedRead resolved;
        try
        {
            resolved = ResolveRead(request);
        }
        catch (FileReadNodeException exception)
        {
            ReportReadError(exception.Code, exception.Message, request, exception.InnerException);
            return;
        }
        catch (FileSystemPathResolutionException exception)
        {
            ReportReadError(exception.Code, exception.Message, request, exception.InnerException);
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
                    request,
                    resolvedPath: resolved.Path);
                return;
            }

            if (ExceedsMaxBytes(fileInfo.Length))
            {
                ReportReadError(
                    FileSystemErrorCodes.FileReadTooLarge,
                    $"file.read file '{request.Path}' exceeds maxBytes.",
                    request,
                    resolvedPath: resolved.Path,
                    bytesRead: fileInfo.Length);
                return;
            }

            var bytes = await File.ReadAllBytesAsync(resolved.Path).ConfigureAwait(false);
            if (ExceedsMaxBytes(bytes.LongLength))
            {
                ReportReadError(
                    FileSystemErrorCodes.FileReadTooLarge,
                    $"file.read file '{request.Path}' exceeds maxBytes.",
                    request,
                    resolvedPath: resolved.Path,
                    bytesRead: bytes.LongLength);
                return;
            }

            var result = CreateResult(request, resolved, bytes);
            await _result.SendAsync(result).ConfigureAwait(false);
            TryEmitDiagnostic(
                FileSystemDiagnosticNames.FileReadSucceeded,
                message: $"Read file '{resolved.Path}'.",
                attributes: CreateAttributes(request, resolved.Path, result.BytesRead, result.Encoding));
        }
        catch (FileReadNodeException exception)
        {
            ReportReadError(
                exception.Code,
                exception.Message,
                request,
                exception.InnerException,
                resolved.Path);
        }
        catch (UnauthorizedAccessException exception)
        {
            ReportReadError(
                FileSystemErrorCodes.FileReadAccessDenied,
                $"file.read access was denied for '{request.Path}'.",
                request,
                exception,
                resolved.Path);
        }
        catch (ArgumentException exception)
        {
            ReportReadError(
                FileSystemErrorCodes.FileReadInvalidPath,
                $"file.read request path is invalid: {exception.Message}",
                request,
                exception,
                resolved.Path);
        }
        catch (NotSupportedException exception)
        {
            ReportReadError(
                FileSystemErrorCodes.FileReadInvalidPath,
                $"file.read request path is invalid: {exception.Message}",
                request,
                exception,
                resolved.Path);
        }
        catch (PathTooLongException exception)
        {
            ReportReadError(
                FileSystemErrorCodes.FileReadInvalidPath,
                $"file.read request path is too long: {exception.Message}",
                request,
                exception,
                resolved.Path);
        }
        catch (FileNotFoundException exception)
        {
            ReportReadError(
                FileSystemErrorCodes.FileReadNotFound,
                $"file.read could not find '{request.Path}'.",
                request,
                exception,
                resolved.Path);
        }
        catch (DirectoryNotFoundException exception)
        {
            ReportReadError(
                FileSystemErrorCodes.FileReadNotFound,
                $"file.read could not find '{request.Path}'.",
                request,
                exception,
                resolved.Path);
        }
        catch (IOException exception)
        {
            ReportReadError(
                FileSystemErrorCodes.FileReadIoFailed,
                $"file.read failed for '{request.Path}': {exception.Message}",
                request,
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
        if (request.ReadAs == FileReadMode.Bytes)
        {
            return new FileReadResult
            {
                Path = resolved.Path,
                Bytes = bytes,
                BytesRead = bytes.LongLength,
                ReadAs = request.ReadAs,
                ReadAt = DateTimeOffset.UtcNow
            };
        }

        return new FileReadResult
        {
            Path = resolved.Path,
            Content = resolved.Encoding!.GetString(bytes),
            Encoding = resolved.EncodingName,
            BytesRead = bytes.LongLength,
            ReadAs = request.ReadAs,
            ReadAt = DateTimeOffset.UtcNow
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

    private void ReportReadError(
        int code,
        string message,
        FileReadRequest request,
        Exception? exception = null,
        string? resolvedPath = null,
        long? bytesRead = null)
    {
        TryReportError(code, message, exception, CreateErrorContext(request, resolvedPath));
        TryEmitDiagnostic(
            FileSystemDiagnosticNames.FileReadFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            CreateAttributes(request, resolvedPath, bytesRead));
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
