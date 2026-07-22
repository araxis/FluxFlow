using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.FileSystem.Nodes;

/// <summary>
/// Canonical file writer that writes the exact original bytes from
/// <see cref="FlowContent"/> and emits expected failures as normal results.
/// </summary>
public sealed class FileWriteNode : IFlowNode
{
    public const string WriteSucceeded = FileSystemDiagnosticNames.FileWriteSucceeded;
    public const string WriteFailed = FileSystemDiagnosticNames.FileWriteFailed;

    private readonly FileWriteOptions _options;
    private readonly TimeProvider _clock;
    private readonly FileSystemOperationPipeline<FileContentWriteRequest, FileWriteResult> _pipeline;

    public FileWriteNode(
        FileWriteOptions? options = null,
        TimeProvider? clock = null)
    {
        _options = ValidateOptions(options ?? new FileWriteOptions());
        _clock = clock ?? TimeProvider.System;
        _pipeline = new FileSystemOperationPipeline<FileContentWriteRequest, FileWriteResult>(
            _options.BoundedCapacity,
            ProcessAsync);
    }

    public ITargetBlock<FlowMessage<FileContentWriteRequest>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<FlowResult<FileWriteResult>>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private async Task<FlowMessage<FlowResult<FileWriteResult>>> ProcessAsync(
        FlowMessage<FileContentWriteRequest> message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var timestamp = _clock.GetUtcNow();
        var request = message.Payload;

        try
        {
            if (request is null)
            {
                throw new FileSystemOperationException(
                    FileSystemErrorCodeNames.WriteContentMissing,
                    "file.write requires an input request.");
            }

            var result = await WriteAsync(request, cancellationToken).ConfigureAwait(false);
            PublishEvent(
                message,
                timestamp,
                WriteSucceeded,
                FlowEventLevel.Information,
                $"Wrote file '{result.Path}'.",
                FileSystemResultKinds.Written,
                isError: false,
                errorCode: null,
                request,
                result.Path,
                result.BytesWritten);
            return message.With(FlowResult<FileWriteResult>.Success(
                FileSystemResultKinds.Written,
                result,
                timestamp));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (TryClassifyFailure(exception, out var failure))
        {
            var error = new DataFlowError(
                failure.Code,
                failure.Message,
                category: "FileSystem",
                isTransient: failure.IsTransient,
                details: CreateErrorDetails(request, failure.ResolvedPath, failure));
            PublishEvent(
                message,
                timestamp,
                WriteFailed,
                FlowEventLevel.Warning,
                error.Message,
                FileSystemResultKinds.WriteFailed,
                isError: true,
                error.Code,
                request,
                failure.ResolvedPath,
                bytesWritten: null);
            return message.With(FlowResult<FileWriteResult>.Failure(
                FileSystemResultKinds.WriteFailed,
                error,
                timestamp));
        }
    }

    private async Task<FileWriteResult> WriteAsync(
        FileContentWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Mode))
        {
            throw new FileSystemOperationException(
                FileSystemErrorCodeNames.WriteUnsupportedMode,
                $"file.write request uses unsupported mode '{request.Mode}'.");
        }

        if (request.Content is null)
        {
            throw new FileSystemOperationException(
                FileSystemErrorCodeNames.WriteContentMissing,
                "file.write requires content.");
        }

        if (!request.Content.HasOriginalRepresentation)
        {
            throw new FileSystemOperationException(
                FileSystemErrorCodeNames.WriteContentUnavailable,
                "file.write requires FlowContent with original bytes.");
        }

        var path = FileSystemPathResolver.Resolve(
            request.Path,
            new FileSystemPathPolicy(
                "file.write",
                _options.BaseDirectory,
                _options.AllowAbsolutePaths,
                FileSystemErrorCodes.FileWriteInvalidPath,
                FileSystemErrorCodes.FileWriteAbsolutePathDenied));
        var bytes = request.Content.OriginalBytes.ToArray();

        if (request.CreateDirectories && Path.GetDirectoryName(path) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);

        switch (request.Mode)
        {
            case FileWriteMode.Overwrite:
                await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
                break;
            case FileWriteMode.Append:
                await using (var stream = new FileStream(
                                 path,
                                 FileMode.Append,
                                 FileAccess.Write,
                                 FileShare.Read,
                                 bufferSize: 4096,
                                 useAsync: true))
                {
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                }

                break;
            case FileWriteMode.CreateNew:
                await using (var stream = new FileStream(
                                 path,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.Read,
                                 bufferSize: 4096,
                                 useAsync: true))
                {
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                }

                break;
        }

        return new FileWriteResult
        {
            Path = path,
            BytesWritten = bytes.LongLength,
            Mode = request.Mode,
            WrittenAt = _clock.GetUtcNow()
        };
    }

    private static bool TryClassifyFailure(
        Exception exception,
        out FileSystemOperationException failure)
    {
        failure = exception switch
        {
            FileSystemOperationException operation => operation,
            FileSystemPathResolutionException path => new FileSystemOperationException(
                path.Code == FileSystemErrorCodes.FileWriteAbsolutePathDenied
                    ? FileSystemErrorCodeNames.WriteAbsolutePathDenied
                    : FileSystemErrorCodeNames.WriteInvalidPath,
                path.Message,
                innerException: path),
            UnauthorizedAccessException access => new FileSystemOperationException(
                FileSystemErrorCodeNames.WriteAccessDenied,
                $"file.write access was denied: {access.Message}",
                innerException: access),
            ArgumentException invalid => new FileSystemOperationException(
                FileSystemErrorCodeNames.WriteInvalidPath,
                $"file.write request path is invalid: {invalid.Message}",
                innerException: invalid),
            NotSupportedException invalid => new FileSystemOperationException(
                FileSystemErrorCodeNames.WriteInvalidPath,
                $"file.write request path is invalid: {invalid.Message}",
                innerException: invalid),
            PathTooLongException invalid => new FileSystemOperationException(
                FileSystemErrorCodeNames.WriteInvalidPath,
                $"file.write request path is too long: {invalid.Message}",
                innerException: invalid),
            IOException io => new FileSystemOperationException(
                FileSystemErrorCodeNames.WriteIoFailed,
                $"file.write failed: {io.Message}",
                isTransient: true,
                innerException: io),
            _ => null!
        };

        return failure is not null;
    }

    private void PublishEvent(
        FlowMessage<FileContentWriteRequest> message,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string text,
        string resultKind,
        bool isError,
        string? errorCode,
        FileContentWriteRequest? request,
        string? resolvedPath,
        long? bytesWritten)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["resultKind"] = resultKind,
            ["isError"] = isError,
            ["path"] = request?.Path,
            ["mode"] = request?.Mode.ToString(),
            ["createDirectories"] = request?.CreateDirectories
        };
        if (errorCode is not null)
            attributes["errorCode"] = errorCode;
        if (resolvedPath is not null)
            attributes["resolvedPath"] = resolvedPath;
        if (bytesWritten.HasValue)
            attributes["bytesWritten"] = bytesWritten.Value;

        _pipeline.PublishEvent(new FlowEvent
        {
            Timestamp = timestamp,
            CorrelationId = message.CorrelationId,
            Name = name,
            Level = level,
            Message = text,
            Attributes = attributes
        });
    }

    private static FlowValue CreateErrorDetails(
        FileContentWriteRequest? request,
        string? resolvedPath,
        Exception exception)
    {
        var content = request?.Content;
        return FlowValue.FromObject(new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["path"] = OptionalValue(request?.Path),
            ["mode"] = OptionalValue(request?.Mode.ToString()),
            ["createDirectories"] = request is null
                ? FlowValue.Null
                : FlowValue.From(request.CreateDirectories),
            ["resolvedPath"] = OptionalValue(resolvedPath),
            ["contentType"] = OptionalValue(content?.ContentType),
            ["encoding"] = OptionalValue(content?.Encoding),
            ["hasOriginalRepresentation"] = content is null
                ? FlowValue.Null
                : FlowValue.From(content.HasOriginalRepresentation),
            ["byteCount"] = content is { HasOriginalRepresentation: true }
                ? FlowValue.From((long)content.OriginalBytes.Length)
                : FlowValue.Null,
            ["exceptionType"] = FlowValue.From(
                exception.GetType().FullName ?? exception.GetType().Name)
        });
    }

    private static FileWriteOptions ValidateOptions(FileWriteOptions options)
    {
        if (options.BoundedCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "boundedCapacity must be positive.");
        return options;
    }

    private static FlowValue OptionalValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? FlowValue.Null : FlowValue.From(value.Trim());
}
