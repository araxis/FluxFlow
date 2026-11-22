using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluxFlow.Release.Tests;
using Microsoft.AspNetCore.Http;
using MQTTnet.Protocol;
using MQTTnet.Server;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed partial class MqttTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Telemetry_published_sample_loads_configuration_delivers_and_shuts_down(bool trial)
    {
#if NET8_0
        const string framework = "net8.0";
#else
        const string framework = "net10.0";
#endif
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var root = ReleaseTestPaths.FindRepositoryRoot();
        var sample = Path.Combine(root, "samples", "Hosting", "FluxFlow.Hosting.Telemetry.Sample");
        var directory = Directory.CreateTempSubdirectory("fluxflow-telemetry-");
        try
        {
            var published = Path.Combine(directory.FullName, "published");
            var publish = StartInfo(root, "publish", sample,
                "-c", configuration, "-f", framework, "--no-build", "--no-restore",
                "--no-self-contained", "-m:1", "-nr:false", "-o", published);
            var publishResult = await ReleaseTestProcess.RunAsync(publish, TimeSpan.FromMinutes(2), "publish telemetry sample");
            publishResult.ExitCode.ShouldBe(0, publishResult.StandardOutput + publishResult.StandardError);
            var sourceSettings = Path.Combine(sample, "appsettings.json");
            File.ReadAllText(Path.Combine(published, "appsettings.json")).ShouldBe(File.ReadAllText(sourceSettings));

            if (trial)
            {
                var settings = File.ReadAllText(Path.Combine(sample, "appsettings.trial.template.json"))
                    .Replace("<UNIQUE_CLIENT_ID>", "json-client-id")
                    .Replace("<MQTT_TOPIC>", "sensors/published/temperature")
                    .Replace("<HTTP_REQUEST_PATH>", "readings")
                    .Replace("<DEVICE_ID>", "trial-device")
                    .Replace("<UNIT>", "trial-unit");
                // Address placeholders are deliberately replaced by environment overrides below.
                File.WriteAllText(Path.Combine(published, "appsettings.Trial.json"), settings);
            }

            await using var broker = await Broker.Start();
            const string username = "trial-mqtt-user";
            const string password = "trial-mqtt-password";
            const string token = "trial-http-token";
            if (trial)
                broker.Server.ValidatingConnectionAsync += args =>
                {
                    if (args.UserName != username || args.Password != password)
                        args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                    return Task.CompletedTask;
                };
            var disconnected = new TaskCompletionSource<MqttClientDisconnectType>(TaskCreationOptions.RunContinuationsAsynchronously);
            broker.Server.ClientDisconnectedAsync += args =>
            {
                if (args.ClientId == broker.ClientId) disconnected.TrySetResult(args.DisconnectType);
                return Task.CompletedTask;
            };
            // Retention makes the reading available when the published host subscribes.
            await broker.Publish("sensors/published/temperature", Encoding.UTF8.GetBytes("24.5"), retain: true);
            var received = new TaskCompletionSource<(JsonElement Body, string MessageId)>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var api = CreateTelemetryApi(async context =>
            {
                if (trial && context.Request.Headers.Authorization.ToString() != $"Bearer {token}")
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                var body = await context.Request.ReadFromJsonAsync<JsonElement>(context.RequestAborted);
                received.TrySetResult((body, context.Request.Headers["X-Flow-Message-Id"].ToString()));
                context.Response.StatusCode = StatusCodes.Status202Accepted;
            });
            await api.StartAsync();
            try
            {
                // Run outside the publish directory to verify the host's content root.
                var run = StartInfo(directory.FullName, Path.Combine(published, "FluxFlow.Hosting.Telemetry.Sample.dll"),
                    "--RunFor=00:00:10",
                    $"--FluxFlow:Resources:broker:ClientId={broker.ClientId}");
                if (trial)
                {
                    run.ArgumentList.Add("--environment=Trial");
                    run.Environment["FluxFlow__Resources__broker__Address"] = $"mqtt://127.0.0.1:{broker.Port}";
                    run.Environment["FluxFlow__Resources__broker__ClientId"] = "environment-client-id";
                    run.Environment["FluxFlow__Resources__broker__Username"] = username;
                    run.Environment["FluxFlow__Resources__broker__Password"] = password;
                    run.Environment["FluxFlow__Resources__api__BaseAddress"] = api.Urls.Single() + "/";
                    run.Environment["FluxFlow__Resources__api__Authentication__Scheme"] = "Bearer";
                    run.Environment["FluxFlow__Resources__api__Authentication__Token"] = token;
                }
                else
                {
                    run.ArgumentList.Add($"--FluxFlow:Resources:broker:Address=mqtt://127.0.0.1:{broker.Port}");
                    run.ArgumentList.Add($"--FluxFlow:Resources:api:BaseAddress={api.Urls.Single()}/");
                }
                var result = await ReleaseTestProcess.RunAsync(run, TimeSpan.FromSeconds(45), "run published telemetry sample");
                var diagnostics = result.StandardOutput + result.StandardError;
                result.ExitCode.ShouldBe(0, diagnostics);
                if (trial)
                {
                    diagnostics.ShouldNotContain(username);
                    diagnostics.ShouldNotContain(password);
                    diagnostics.ShouldNotContain(token);
                }
                received.Task.IsCompletedSuccessfully.ShouldBeTrue(diagnostics);
                var reading = await received.Task;
                reading.Body.GetProperty("deviceId").GetString().ShouldBe(trial ? "trial-device" : "published");
                reading.Body.GetProperty("temperature").GetDecimal().ShouldBe(24.5m);
                reading.Body.GetProperty("unit").GetString().ShouldBe(trial ? "trial-unit" : "C");
                reading.MessageId.ShouldNotBeNullOrWhiteSpace();
                reading.Body.GetProperty("messageId").GetString().ShouldBe(reading.MessageId);
                reading.Body.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
                (await disconnected.Task.WaitAsync(Deadline)).ShouldBe(MqttClientDisconnectType.Clean);
                (await broker.Server.GetClientsAsync()).ShouldNotContain(client => client.Id == broker.ClientId);
            }
            finally { await api.StopAsync().WaitAsync(Deadline); }
        }
        finally { directory.Delete(recursive: true); }

        static ProcessStartInfo StartInfo(string workingDirectory, params string[] arguments)
        {
            var info = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            // Keep developer trial overrides out of child processes; never mutate the test process environment.
            foreach (var key in info.Environment.Keys.Where(key =>
                key.StartsWith("FluxFlow__", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("FluxFlow:", StringComparison.OrdinalIgnoreCase)
                || key.Equals("RunFor", StringComparison.OrdinalIgnoreCase)).ToArray())
                info.Environment.Remove(key);
            info.Environment["DOTNET_ENVIRONMENT"] = "Production";
            return info;
        }
    }
}
