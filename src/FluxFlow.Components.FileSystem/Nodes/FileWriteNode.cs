using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Components.FileSystem.Timing;
using FluxFlow.Engine.Components;
using FluxFlow.Engine.Runtime;
using System.Text;
using System.Threading.Tasks.Dataflow;

namespace FluxFlow.Components.FileSystem.Nodes;

public sealed class FileWriteNode : FlowNodeBase
{
    private readonly FileWriteOptions _options;
    private readonly IFileSystemClock _clock;
    private readonly ActionBlock<FileWriteRequest> _input;
    private readonly BufferBlock<FileWriteResult> _result;

    private FileWriteNode(
        FileWriteOptions options,
        IFileSystemClock clock)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (options.BoundedCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "File write bounded capacity must be greater than zero.");
        }

        _input = new ActionBlock<FileWriteRequest>(
            WriteAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = options.BoundedCapacity,
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
        _result = new BufferBlock<FileWriteResult>(
            new DataflowBlockOptions { BoundedCapacity = options.BoundedCapacity });
        CompleteWhen(_input.Completion);
    }

    public ITargetBlock<FileWriteRequest> Input => _input;

    public ISourceBlock<FileWriteResult> Result => _result;

    public static RuntimeNode Create(RuntimeNodeFactoryContext context)
        => Create(context, new FileSystemComponentOptions());

    public static RuntimeNode Create(
        RuntimeNodeFactoryContext context,
        FileSystemComponentOptions componentOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(componentOptions);

        var options = FileSystemOptionsReader.ReadFileWriteOptions(context.Definition);
        var node = new FileWriteNode(options, componentOptions.Clock);

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

    private async Task WriteAsync(FileWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ResolvedWrite resolved;
        try
        {
            resolved = ResolveWrite(request);
        }
        catch (FileWriteNodeException exception)
        {
            ReportWriteError(exception.Code, exception.Message, request, exception.InnerException);
            return;
        }
        catch (FileSystemPathResolutionException exception)
        {
            ReportWriteError(exception.Code, exception.Message, request, exception.InnerException);
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
                    await File.WriteAllBytesAsync(resolved.Path, resolved.Bytes).ConfigureAwait(false);
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
                        await stream.WriteAsync(resolved.Bytes).ConfigureAwait(false);
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
                        await stream.WriteAsync(resolved.Bytes).ConfigureAwait(false);
                    }

                    break;
                default:
                    ReportWriteError(
                        FileSystemErrorCodes.FileWriteUnsupportedMode,
                        $"file.write request uses unsupported mode '{request.Mode}'.",
                        request);
                    return;
            }

            var result = new FileWriteResult
            {
                Path = resolved.Path,
                BytesWritten = resolved.Bytes.Length,
                Mode = request.Mode,
                WrittenAt = _clock.UtcNow
            };

            await _result.SendAsync(result).ConfigureAwait(false);
            TryEmitDiagnostic(
                FileSystemDiagnosticNames.FileWriteSucceeded,
                message: $"Wrote file '{resolved.Path}'.",
                attributes: CreateAttributes(request, resolved.Path, resolved.Bytes.Length));
        }
        catch (UnauthorizedAccessException exception)
        {
            ReportWriteError(
                FileSystemErrorCodes.FileWriteAccessDenied,
                $"file.write access was denied for '{request.Path}'.",
                request,
                exception);
        }
        catch (ArgumentException exception)
        {
            ReportWriteError(
                FileSystemErrorCodes.FileWriteInvalidPath,
                $"file.write request path is invalid: {exception.Message}",
                request,
                exception);
        }
        catch (NotSupportedException exception)
        {
            ReportWriteError(
                FileSystemErrorCodes.FileWriteInvalidPath,
                $"file.write request path is invalid: {exception.Message}",
                request,
                exception);
        }
        catch (PathTooLongException exception)
        {
            ReportWriteError(
                FileSystemErrorCodes.FileWriteInvalidPath,
                $"file.write request path is too long: {exception.Message}",
                request,
                exception);
        }
        catch (IOException exception)
        {
            ReportWriteError(
                FileSystemErrorCodes.FileWriteIoFailed,
                $"file.write failed for '{request.Path}': {exception.Message}",
                request,
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

    private void ReportWriteError(
        int code,
        string message,
        FileWriteRequest request,
        Exception? exception = null)
    {
        TryReportError(code, message, exception, CreateErrorContext(request));
        TryEmitDiagnostic(
            FileSystemDiagnosticNames.FileWriteFailed,
            FlowDiagnosticLevel.Error,
            message,
            exception,
            CreateAttributes(request));
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
