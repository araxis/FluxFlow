using System.Net;
using OpenTelemetry.Exporter;

namespace FluxFlow.Execution;

// The SDK owns OTLP serialization. Its HTTP transport hands each synchronous export
// to the current run's bounded store without a loopback network request.
internal sealed class ExecutionOtlpCapture : HttpMessageHandler
{
    private readonly AsyncLocal<Action<byte[]>?> _destination = new();

    public OtlpExporterOptions Options(string signal) => new()
    {
        Protocol = OtlpExportProtocol.HttpProtobuf,
        Endpoint = new Uri($"http://localhost/v1/{signal}"),
        HttpClientFactory = () => new HttpClient(this, disposeHandler: false)
    };

    public IDisposable Capture(Action<byte[]> destination)
    {
        var scope = new Scope(_destination, _destination.Value);
        _destination.Value = destination;
        return scope;
    }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = request.Content!.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult();
        _destination.Value!(bytes);
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        response.Content.Headers.ContentType = new("application/x-protobuf");
        return response;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Send(request, cancellationToken));

    private sealed class Scope(AsyncLocal<Action<byte[]>?> current, Action<byte[]>? previous) : IDisposable
    {
        public void Dispose() => current.Value = previous;
    }
}
