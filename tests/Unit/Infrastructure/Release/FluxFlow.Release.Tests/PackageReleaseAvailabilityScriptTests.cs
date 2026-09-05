using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace FluxFlow.Release.Tests;

[Collection(ReleaseProcessCollection.Name)]
public sealed class PackageReleaseAvailabilityScriptTests
{
    private static readonly IReadOnlyDictionary<string, string?> LoopbackEnvironment =
        new Dictionary<string, string?>
        {
            ["NO_PROXY"] = "127.0.0.1,localhost",
            ["no_proxy"] = "127.0.0.1,localhost"
        };

    [Fact]
    public async Task Availability_reports_missing_package_from_local_v3_source()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetNodesPackage(root);
        var version = ReadProjectVersion(root, package);
        await using var source = new PackageV3TestSource(
            package.PackageId,
            version,
            packagePresent: false);

        var result = await RunAvailabilityAsync(
            root,
            package,
            version,
            source.IndexUrl,
            "Missing");

        result.ExitCode.ShouldBe(0, result.ToString());
        result.StandardOutput.ShouldContain($"PACKAGE_ALIAS={package.Alias}");
        result.StandardOutput.ShouldContain($"PACKAGE_ID={package.PackageId}");
        result.StandardOutput.ShouldContain($"PACKAGE_VERSION={version}");
        result.StandardOutput.ShouldContain("PACKAGE_AVAILABILITY=Missing");
        source.RequestPaths.Any(path => path.Contains(
            package.PackageId.ToLowerInvariant(),
            StringComparison.Ordinal)).ShouldBeTrue(
                "The availability check must query the resolved package id.");
    }

    [Fact]
    public async Task Availability_reports_present_package_from_local_v3_source()
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetNodesPackage(root);
        var version = ReadProjectVersion(root, package);
        await using var source = new PackageV3TestSource(
            package.PackageId,
            version,
            packagePresent: true);

        var result = await RunAvailabilityAsync(
            root,
            package,
            version,
            source.IndexUrl,
            "Present");

        result.ExitCode.ShouldBe(0, result.ToString());
        result.StandardOutput.ShouldContain($"PACKAGE_ALIAS={package.Alias}");
        result.StandardOutput.ShouldContain($"PACKAGE_ID={package.PackageId}");
        result.StandardOutput.ShouldContain($"PACKAGE_VERSION={version}");
        result.StandardOutput.ShouldContain("PACKAGE_AVAILABILITY=Present");
    }

    [Theory]
    [InlineData(false, "Present", "Missing")]
    [InlineData(true, "Missing", "Present")]
    public async Task Availability_expected_state_mismatch_fails_closed(
        bool packagePresent,
        string expectedState,
        string observedState)
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetNodesPackage(root);
        var version = ReadProjectVersion(root, package);
        await using var source = new PackageV3TestSource(
            package.PackageId,
            version,
            packagePresent);

        var result = await RunAvailabilityAsync(
            root,
            package,
            version,
            source.IndexUrl,
            expectedState);

        result.ExitCode.ShouldNotBe(0);
        result.ToString().ShouldContain(expectedState);
        result.ToString().ShouldContain(observedState);
    }

    [Theory]
    [InlineData(V3FailureMode.HttpError)]
    [InlineData(V3FailureMode.InvalidServiceIndex)]
    [InlineData(V3FailureMode.Disconnect)]
    public async Task Availability_unusable_v3_source_fails_closed(V3FailureMode failureMode)
    {
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var package = GetNodesPackage(root);
        var version = ReadProjectVersion(root, package);
        await using var source = new PackageV3TestSource(
            package.PackageId,
            version,
            packagePresent: true,
            failureMode);

        var result = await RunAvailabilityAsync(
            root,
            package,
            version,
            source.IndexUrl,
            expectedState: null);

        result.ExitCode.ShouldNotBe(0);
        result.StandardOutput.ShouldNotContain("PACKAGE_AVAILABILITY=");
    }

    private static async Task<ReleaseScriptResult> RunAvailabilityAsync(
        string root,
        PackageManifestEntry package,
        string version,
        string packageSource,
        string? expectedState)
    {
        var arguments = new List<string>
        {
            "-Package",
            package.Alias,
            "-Version",
            version,
            "-ManifestPath",
            Path.Combine(root, "eng", "packages.json"),
            "-PackageSource",
            packageSource
        };
        if (expectedState is not null)
        {
            arguments.Add("-ExpectedState");
            arguments.Add(expectedState);
        }

        return await ReleaseScriptRunner.RunAsync(
            root,
            "package-release-availability.ps1",
            LoopbackEnvironment,
            [.. arguments]);
    }

    private static PackageManifestEntry GetNodesPackage(string root)
        => PackageManifest
            .Read(root)
            .Single(entry => entry.Alias == "nodes");

    private static string ReadProjectVersion(string root, PackageManifestEntry package)
    {
        var projectPath = Path.Combine(root, NormalizePath(package.Project));
        var project = XDocument.Load(projectPath);
        return project
            .Descendants()
            .Where(element => element.Name.LocalName == "Version")
            .Select(element => element.Value.Trim())
            .First(value => value.Length > 0);
    }

    private static string NormalizePath(string path)
        => path
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

    public enum V3FailureMode
    {
        None,
        HttpError,
        InvalidServiceIndex,
        Disconnect
    }

    private sealed class PackageV3TestSource : IAsyncDisposable
    {
        private readonly string _packageId;
        private readonly string _version;
        private readonly bool _packagePresent;
        private readonly V3FailureMode _failureMode;
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stopping = new();
        private readonly object _gate = new();
        private readonly List<string> _requestPaths = [];
        private readonly Task _server;

        public PackageV3TestSource(
            string packageId,
            string version,
            bool packagePresent,
            V3FailureMode failureMode = V3FailureMode.None)
        {
            _packageId = packageId.ToLowerInvariant();
            _version = version.ToLowerInvariant();
            _packagePresent = packagePresent;
            _failureMode = failureMode;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseUrl = $"http://127.0.0.1:{endpoint.Port}/";
            IndexUrl = $"{BaseUrl}v3/index.json";
            _server = ServeAsync();
        }

        public string BaseUrl { get; }

        public string IndexUrl { get; }

        public IReadOnlyList<string> RequestPaths
        {
            get
            {
                lock (_gate)
                {
                    return _requestPaths.ToArray();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stopping.Cancel();
            _listener.Stop();
            try
            {
                await _server.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
            }
            catch (SocketException) when (_stopping.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_stopping.IsCancellationRequested)
            {
            }
            finally
            {
                _stopping.Dispose();
            }
        }

        private async Task ServeAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                using var client = await _listener
                    .AcceptTcpClientAsync(_stopping.Token)
                    .ConfigureAwait(false);
                await HandleAsync(client, _stopping.Token).ConfigureAwait(false);
            }
        }

        private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: true);
            var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            string? header;
            do
            {
                header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            while (!string.IsNullOrEmpty(header));

            var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var method = requestParts[0];
            var requestTarget = requestParts[1];
            var path = Uri.TryCreate(requestTarget, UriKind.Absolute, out var absolute)
                ? absolute.AbsolutePath
                : requestTarget.Split('?', 2)[0];
            lock (_gate)
            {
                _requestPaths.Add(path);
            }

            if (_failureMode == V3FailureMode.Disconnect)
            {
                return;
            }

            if (path.Equals("/v3/index.json", StringComparison.Ordinal))
            {
                if (_failureMode == V3FailureMode.HttpError)
                {
                    await WriteResponseAsync(
                        stream,
                        method,
                        503,
                        "Service Unavailable",
                        "service unavailable",
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                var body = _failureMode == V3FailureMode.InvalidServiceIndex
                    ? "{\"version\":\"3.0.0\",\"resources\":[]}"
                    : $$"""
                        {"version":"3.0.0","resources":[{"@id":"{{BaseUrl}}flat/","@type":"PackageBaseAddress/3.0.0"}]}
                        """;
                await WriteResponseAsync(
                    stream,
                    method,
                    200,
                    "OK",
                    body,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var versionIndexPath = $"/flat/{_packageId}/index.json";
            if (path.Equals(versionIndexPath, StringComparison.Ordinal))
            {
                var body = JsonSerializer.Serialize(new
                {
                    versions = _packagePresent ? new[] { _version } : []
                });
                await WriteResponseAsync(
                    stream,
                    method,
                    200,
                    "OK",
                    body,
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            var packagePath = $"/flat/{_packageId}/{_version}/{_packageId}.{_version}.nupkg";
            await WriteResponseAsync(
                stream,
                method,
                path.Equals(packagePath, StringComparison.Ordinal) && _packagePresent ? 200 : 404,
                path.Equals(packagePath, StringComparison.Ordinal) && _packagePresent ? "OK" : "Not Found",
                _packagePresent ? "package" : "not found",
                cancellationToken).ConfigureAwait(false);
        }

        private static async Task WriteResponseAsync(
            NetworkStream stream,
            string method,
            int statusCode,
            string reason,
            string body,
            CancellationToken cancellationToken)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(body);
            var headers = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {statusCode} {reason}\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(headers, cancellationToken).ConfigureAwait(false);
            if (!method.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
            {
                await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
