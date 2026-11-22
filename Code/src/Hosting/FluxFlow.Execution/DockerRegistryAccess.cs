using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using FluxFlow.Environments;

namespace FluxFlow.Execution;

public sealed class DockerRegistryAccess(HttpClient client, string? configDirectory = null) : IRegistryAccess
{
    public async Task<RegistryAccessResult> TestAsync(ImageRegistry registry, CancellationToken cancellationToken)
    {
        registry.Validate();
        var address = registry.Address == "docker.io" ? "registry-1.docker.io" : registry.Address;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await client.GetAsync($"https://{address}/v2/", HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        return response.StatusCode == HttpStatusCode.Unauthorized
            ? new(true, "Registry reachable. Authentication is required; this check does not validate saved credentials.")
            : new(response.IsSuccessStatusCode, response.IsSuccessStatusCode ? "Registry reachable." : $"Registry returned HTTP {(int)response.StatusCode}.");
    }

    public async Task<RegistryAccessResult> SignInAsync(RegistrySignIn request, CancellationToken cancellationToken)
    {
        request.Registry.Validate();
        if (request.Registry.Authentication != "Docker credentials") throw new ArgumentException("Choose Docker credentials to sign in.");
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password)) throw new ArgumentException("Enter a username and password or access token.");
        var directory = configDirectory ?? Environment.GetEnvironmentVariable("DOCKER_CONFIG") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".docker");
        var path = Path.Combine(directory, "config.json");
        var config = File.Exists(path) ? JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)) : null;
        var server = request.Registry.Address is "docker.io" or "index.docker.io" or "registry-1.docker.io" ? "https://index.docker.io/v1/" : request.Registry.Address;
        var helper = config?["credHelpers"]?[server]?.GetValue<string>() ?? config?["credsStore"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(helper))
            return new(false, "Configure a Docker credential helper first. Studio will not save a password in Docker's plain configuration file.");
        return await RunAsync(["--config", directory, "login", request.Registry.Address, "--username", request.Username, "--password-stdin"], request.Password, "Signed in using Docker's credential store.", cancellationToken).ConfigureAwait(false);
    }

    public Task<RegistryAccessResult> SignOutAsync(ImageRegistry registry, CancellationToken cancellationToken)
    {
        registry.Validate();
        return RunAsync(["logout", registry.Address], null, "Signed out. Docker credentials for this registry were removed.", cancellationToken);
    }

    private static async Task<RegistryAccessResult> RunAsync(string[] arguments, string? password, string success, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo("docker") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Docker CLI could not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            if (password is not null) await process.StandardInput.WriteLineAsync(password.AsMemory(), deadline.Token).ConfigureAwait(false);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            // CLI output can contain authentication details; return only the operation status.
            return new(process.ExitCode == 0, process.ExitCode == 0 ? success : "Docker could not complete the registry operation. Check the address, credentials and Docker credential helper.");
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
        }
    }
}
