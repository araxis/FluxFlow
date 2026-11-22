namespace FluxFlow.Components.Http;

/// <summary>A local file streamed as a named multipart form part.</summary>
public sealed class HttpFormFile
{
    /// <summary>The form field name expected by the server. Multiple files can share it.</summary>
    public required string Name { get; init; }
    /// <summary>Path on the runner machine; relative paths use the process working directory.</summary>
    public required string FilePath { get; init; }
    /// <summary>Filename sent to the server. Defaults to the local file's basename.</summary>
    public string? FileName { get; init; }
    /// <summary>Media type of this file part. Defaults to application/octet-stream.</summary>
    public string? ContentType { get; init; }
}
