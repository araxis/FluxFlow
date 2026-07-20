using System.Text;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.FileSystem.Contracts;
using FluxFlow.Components.FileSystem.Diagnostics;
using FluxFlow.Components.FileSystem.Options;
using FluxFlow.Data;
using FluxFlow.Nodes;
using DataFlowError = FluxFlow.Data.FlowError;

namespace FluxFlow.Components.FileSystem.Nodes;

/// <summary>
/// Canonical file reader that preserves file bytes as <see cref="FlowContent"/>
/// and emits expected read failures as normal results.
/// </summary>
public sealed class FlowContentFileReadNode : IFlowNode
{
    public const string ReadSucceeded = FileSystemDiagnosticNames.FileReadSucceeded;
    public const string ReadFailed = FileSystemDiagnosticNames.FileReadFailed;

    private const string TextContentType = "text/plain";
    private const string BinaryContentType = "application/octet-stream";

    private readonly FileReadOptions _options;
    private readonly TimeProvider _clock;
    private readonly FileSystemOperationPipeline<FileReadRequest, FileReadContent> _pipeline;

    public FlowContentFileReadNode(
        FileReadOptions? options = null,
        TimeProvider? clock = null)
    {
        _options = ValidateOptions(options ?? new FileReadOptions());
        _clock = clock ?? TimeProvider.System;
        _pipeline = new FileSystemOperationPipeline<FileReadRequest, FileReadContent>(
            _options.BoundedCapacity,
            ProcessAsync);
    }

    public ITargetBlock<FlowMessage<FileReadRequest>> Input => _pipeline.Input;

    public ISourceBlock<FlowMessage<FlowResult<FileReadContent>>> Output => _pipeline.Output;

    public ISourceBlock<FlowEvent> Events => _pipeline.Events;

    public Task Completion => _pipeline.Completion;

    public void Complete() => _pipeline.Complete();

    public void Fault(Exception exception) => _pipeline.Fault(exception);

    public ValueTask DisposeAsync() => _pipeline.DisposeAsync();

    private async Task<FlowMessage<FlowResult<FileReadContent>>> ProcessAsync(
        FlowMessage<FileReadRequest> message,
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
                    FileSystemErrorCodeNames.ReadInvalidPath,
                    "file.read requires an input request.");
            }

            var result = await ReadAsync(request, cancellationToken).ConfigureAwait(false);
            PublishEvent(
                message,
                timestamp,
                ReadSucceeded,
                FlowEventLevel.Information,
                $"Read file '{result.Path}'.",
                FileSystemResultKinds.Read,
                isError: false,
                errorCode: null,
                request,
                result.Path,
                result.BytesRead);
            return message.With(FlowResult<FileReadContent>.Success(
                FileSystemResultKinds.Read,
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
                details: CreateErrorDetails(
                    request,
                    failure.ResolvedPath,
                    failure.BytesRead,
                    failure));
            PublishEvent(
                message,
                timestamp,
                ReadFailed,
                FlowEventLevel.Warning,
                error.Message,
                FileSystemResultKinds.ReadFailed,
                isError: true,
                error.Code,
                request,
                failure.ResolvedPath,
                failure.BytesRead);
            return message.With(FlowResult<FileReadContent>.Failure(
                FileSystemResultKinds.ReadFailed,
                error,
                timestamp));
        }
    }

    private async Task<FileReadContent> ReadAsync(
        FileReadRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.ReadAs))
        {
            throw new FileSystemOperationException(
                FileSystemErrorCodeNames.ReadUnsupportedMode,
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

        var encoding = request.ReadAs == FileReadMode.Text
            ? ResolveEncodingName(request.Encoding)
            : null;
        var contentType = string.IsNullOrWhiteSpace(request.ContentType)
            ? request.ReadAs == FileReadMode.Text ? TextContentType : BinaryContentType
            : request.ContentType.Trim();

        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        var read = await BoundedFileReader.ReadAsync(stream, _options.MaxBytes, cancellationToken)
            .ConfigureAwait(false);
        if (read.LimitExceeded)
        {
            throw new FileSystemOperationException(
                FileSystemErrorCodeNames.ReadTooLarge,
                $"file.read file '{request.Path}' exceeds maxBytes.",
                resolvedPath: path,
                bytesRead: read.BytesRead);
        }

        return new FileReadContent
        {
            Path = path,
            Content = FlowContent.FromBytes(read.Bytes, contentType, encoding),
            BytesRead = read.Bytes.LongLength,
            ReadAs = request.ReadAs,
            ReadAt = _clock.GetUtcNow()
        };
    }

    private string ResolveEncodingName(string? requestedEncoding)
    {
        var encodingName = string.IsNullOrWhiteSpace(requestedEncoding)
            ? _options.DefaultEncoding
            : requestedEncoding.Trim();
        try
        {
            return Encoding.GetEncoding(encodingName).WebName;
        }
        catch (ArgumentException exception)
        {
            throw new FileSystemOperationException(
                FileSystemErrorCodeNames.ReadUnsupportedEncoding,
                $"file.read request uses unsupported encoding '{encodingName}'.",
                innerException: exception);
        }
    }

    private static bool TryClassifyFailure(
        Exception exception,
        out FileSystemOperationException failure)
    {
        failure = exception switch
        {
            FileSystemOperationException operation => operation,
            FileSystemPathResolutionException path => new FileSystemOperationException(
                path.Code == FileSystemErrorCodes.FileReadAbsolutePathDenied
                    ? FileSystemErrorCodeNames.ReadAbsolutePathDenied
                    : FileSystemErrorCodeNames.ReadInvalidPath,
                path.Message,
                innerException: path),
            UnauthorizedAccessException access => new FileSystemOperationException(
                FileSystemErrorCodeNames.ReadAccessDenied,
                $"file.read access was denied: {access.Message}",
                innerException: access),
            FileNotFoundException missing => new FileSystemOperationException(
                FileSystemErrorCodeNames.ReadNotFound,
                $"file.read could not find the requested file: {missing.Message}",
                innerException: missing),
            DirectoryNotFoundException missing => new FileSystemOperationException(
                FileSystemErrorCodeNames.ReadNotFound,
                $"file.read could not find the requested file: {missing.Message}",
                innerException: missing),
            ArgumentException invalid => new FileSystemOperationException(
                FileSystemErrorCodeNames.ReadInvalidPath,
                $"file.read request path is invalid: {invalid.Message}",
                innerException: invalid),
            NotSupportedException invalid => new FileSystemOperationException(
                FileSystemErrorCodeNames.ReadInvalidPath,
                $"file.read request path is invalid: {invalid.Message}",
                innerException: invalid),
            PathTooLongException invalid => new FileSystemOperationException(
                FileSystemErrorCodeNames.ReadInvalidPath,
                $"file.read request path is too long: {invalid.Message}",
                innerException: invalid),
            IOException io => new FileSystemOperationException(
                FileSystemErrorCodeNames.ReadIoFailed,
                $"file.read failed: {io.Message}",
                isTransient: true,
                innerException: io),
            _ => null!
        };

        return failure is not null;
    }

    private void PublishEvent(
        FlowMessage<FileReadRequest> message,
        DateTimeOffset timestamp,
        string name,
        FlowEventLevel level,
        string text,
        string resultKind,
        bool isError,
        string? errorCode,
        FileReadRequest? request,
        string? resolvedPath,
        long? bytesRead)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["resultKind"] = resultKind,
            ["isError"] = isError,
            ["path"] = request?.Path,
            ["readAs"] = request?.ReadAs.ToString()
        };
        if (errorCode is not null)
            attributes["errorCode"] = errorCode;
        if (resolvedPath is not null)
            attributes["resolvedPath"] = resolvedPath;
        if (bytesRead.HasValue)
            attributes["bytesRead"] = bytesRead.Value;

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

    private FlowValue CreateErrorDetails(
        FileReadRequest? request,
        string? resolvedPath,
        long? bytesRead,
        Exception exception)
    {
        var details = new Dictionary<string, FlowValue>(StringComparer.Ordinal)
        {
            ["path"] = OptionalValue(request?.Path),
            ["readAs"] = OptionalValue(request?.ReadAs.ToString()),
            ["contentType"] = OptionalValue(request?.ContentType),
            ["encoding"] = OptionalValue(request?.Encoding),
            ["resolvedPath"] = OptionalValue(resolvedPath),
            ["bytesRead"] = bytesRead.HasValue ? FlowValue.From(bytesRead.Value) : FlowValue.Null,
            ["maxBytes"] = _options.MaxBytes.HasValue
                ? FlowValue.From(_options.MaxBytes.Value)
                : FlowValue.Null,
            ["exceptionType"] = FlowValue.From(
                exception.GetType().FullName ?? exception.GetType().Name)
        };
        return FlowValue.FromObject(details);
    }

    private static FileReadOptions ValidateOptions(FileReadOptions options)
    {
        if (options.BoundedCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "boundedCapacity must be positive.");
        if (options.MaxBytes is <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "maxBytes must be positive when set.");
        if (string.IsNullOrWhiteSpace(options.DefaultEncoding))
            throw new ArgumentException("defaultEncoding cannot be empty.", nameof(options));

        try
        {
            Encoding.GetEncoding(options.DefaultEncoding);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException("defaultEncoding is not supported.", nameof(options), exception);
        }

        return options;
    }

    private static FlowValue OptionalValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? FlowValue.Null : FlowValue.From(value.Trim());
}
