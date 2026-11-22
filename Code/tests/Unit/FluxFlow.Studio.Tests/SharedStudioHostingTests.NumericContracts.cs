using System.Globalization;
using System.Text.Json;
using FluxFlow.Authoring;
using FluxFlow.Studio.UI.Features.Connections;

using FluxFlow.Studio.UI.Features.Designer.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor;
using Shouldly;
using Xunit;

namespace FluxFlow.Studio.Tests;

public sealed partial class SharedStudioHostingTests
{
    [Theory]
    [InlineData("requestReply", "BufferCapacity", 0L, int.MaxValue)]
    [InlineData("batch", "BatchSize", 1L, int.MaxValue)]
    [InlineData("serial.text", "MaxLineBytes", 1L, int.MaxValue - 1)]
    [InlineData("serial.text", "InputCapacity", 1L, int.MaxValue)]
    [InlineData("ros.publisher", "OutputCapacity", 1L, int.MaxValue)]
    [InlineData("ros.subscriber", "OutputCapacity", 1L, int.MaxValue)]
    [InlineData("ros.subscriber", "QueueSize", 1L, int.MaxValue)]
    [InlineData("serial.text", "OutputCapacity", 1L, int.MaxValue)]
    [InlineData("serial.reader", "FrameLength", 1L, 1048576L)]
    [InlineData("serial.reader", "ReadBufferBytes", 1L, 65536L)]
    [InlineData("serial.reader", "Capacity", 1L, 65536L)]
    [InlineData("serial.reader", "MaxFrameBytes", 1L, 1048576L)]
    [InlineData("serial.writer", "FrameLength", 1L, 1048576L)]
    [InlineData("serial.writer", "MaxFrameBytes", 1L, 1048576L)]
    [InlineData("serial.writer", "Capacity", 1L, 65536L)]
    [InlineData("serial.writer", "ReadBufferBytes", 1L, 65536L)]
    [InlineData("amqp.consumer", "Prefetch", 1L, ushort.MaxValue)]
    [InlineData("http.trigger", "AcknowledgementStatusCode", 200L, 299L)]
    [InlineData("ble.server", "OutputCapacity", 1L, int.MaxValue)]
    [InlineData("retry", "MaxRetryAttempts", 1L, int.MaxValue)]
    [InlineData("connection:mqtt.client", "SessionExpiryInterval", 0L, uint.MaxValue)]
    [InlineData("connection:mqtt.client", "Port", 1L, 65535L)]
    [InlineData("connection:serial.port", "DataBits", 5L, 8L)]
    [InlineData("connection:serial.port", "BaudRate", 1L, int.MaxValue)]
    [InlineData("connection:http.client", "MaxResponseContentBufferSize", 1L, int.MaxValue)]
    [InlineData("connection:nats.connection", "MaxReconnectAttempts", 1L, 1000L)]
    [InlineData("nats.publisher", "TimeoutSeconds", 1L, 600L)]
    [InlineData("nats.publisher", "Capacity", 1L, 65536L)]
    [InlineData("nats.publisher", "MaxMessageBytes", 1L, 67108864L)]
    [InlineData("nats.subscriber", "TimeoutSeconds", 1L, 600L)]
    [InlineData("nats.subscriber", "Capacity", 1L, 65536L)]
    [InlineData("nats.subscriber", "MaxMessageBytes", 1L, 67108864L)]
    [InlineData("nats.request", "TimeoutSeconds", 1L, 600L)]
    [InlineData("nats.request", "Capacity", 1L, 65536L)]
    [InlineData("nats.request", "MaxMessageBytes", 1L, 67108864L)]
    [InlineData("nats.reply", "TimeoutSeconds", 1L, 600L)]
    [InlineData("nats.reply", "Capacity", 1L, 65536L)]
    [InlineData("nats.reply", "MaxMessageBytes", 1L, 67108864L)]
    [InlineData("nats.jetstream.publisher", "TimeoutSeconds", 1L, 600L)]
    [InlineData("nats.jetstream.publisher", "Capacity", 1L, 65536L)]
    [InlineData("nats.jetstream.publisher", "MaxMessageBytes", 1L, 67108864L)]
    [InlineData("nats.jetstream.publisher", "MaxStreamAgeSeconds", 1L, 31536000L)]
    [InlineData("nats.jetstream.consumer", "TimeoutSeconds", 1L, 600L)]
    [InlineData("nats.jetstream.consumer", "Capacity", 1L, 65536L)]
    [InlineData("nats.jetstream.consumer", "MaxMessageBytes", 1L, 67108864L)]
    [InlineData("nats.jetstream.consumer", "MaxStreamAgeSeconds", 1L, 31536000L)]
    [InlineData("nats.jetstream.consumer", "MaxInFlight", 1L, 1024L)]
    [InlineData("nats.jetstream.consumer", "AckWaitSeconds", 1L, 86400L)]
    [InlineData("nats.jetstream.consumer", "MaxDeliver", 1L, 10000L)]
    [InlineData("nats.jetstream.settlement", "Capacity", 1L, 65536L)]
    public async Task Numeric_contract_fields_reject_fractions_and_overflow_without_rewriting_saved_values(string type, string property, long minimum, long maximum)
    {
        var services = CreateServices(); var components = new CapturingComponents(); services.AddSingleton<IComponentActivator>(components);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        ConnectionDescription? connection = type switch
        {
            "connection:mqtt.client" => StudioTestServices.MqttConnection(),
            "connection:serial.port" => StudioTestServices.SerialConnection(),
            "connection:http.client" => StudioTestServices.HttpConnection(),
            "connection:nats.connection" => StudioTestServices.NatsConnection(),
            _ => null
        };
        var options = connection is null
            ? scope.ServiceProvider.GetServices<IComponentDescription>().Single(description => description.Palette.ComponentType == type).Inspector.Sections.SelectMany(section => section.Options)
            : connection.PropertyGroups is null ? connection.Options : connection.PropertyGroups(new() { ["Host"] = "localhost" }).SelectMany(group => group.Options);
        var option = options.Single(item => item.Name == property);
        option.Editor.ShouldBe(OptionEditorKind.Integer);
        option.Min.ShouldBe(minimum); option.Max.ShouldBe(maximum);
        var saved = "00" + (minimum + 1).ToString(CultureInfo.InvariantCulture);
        var key = property.ToLowerInvariant();
        var configuration = new Dictionary<string, JsonElement>
        {
            [key] = JsonSerializer.SerializeToElement(saved),
            ["Future"] = JsonSerializer.SerializeToElement(new { Keep = true })
        };
        var before = JsonSerializer.Serialize(configuration); var commits = 0;
        await using var renderer = new HtmlRenderer(scope.ServiceProvider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<OptionEditorField>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(OptionEditorField.Option)] = option,
                [nameof(OptionEditorField.Configuration)] = configuration,
                [nameof(OptionEditorField.OnChanged)] = EventCallback.Factory.Create(this, () => commits++)
            }));
            var input = components.Items.OfType<MudTextField<string>>().Single();
            var field = components.Items.OfType<ValidatedTextField>().Single();
            input.Value.ShouldBe(saved);
            foreach (var invalid in new[] { "1.5", "NaN", "1e3", (minimum - 1).ToString(CultureInfo.InvariantCulture), (maximum + 1).ToString(CultureInfo.InvariantCulture) })
            {
                field.Validate(invalid).ShouldNotBeNull();
                await input.ValueChanged.InvokeAsync(invalid);
                JsonSerializer.Serialize(configuration).ShouldBe(before); commits.ShouldBe(0);
            }
            if (option.IsRequired)
            {
                field.Validate("").ShouldNotBeNull();
                await input.ValueChanged.InvokeAsync("");
                JsonSerializer.Serialize(configuration).ShouldBe(before); commits.ShouldBe(0);
            }
            await input.ValueChanged.InvokeAsync((minimum + 1).ToString(CultureInfo.InvariantCulture));
            JsonSerializer.Serialize(configuration).ShouldBe(before); commits.ShouldBe(0);
            await input.ValueChanged.InvokeAsync(maximum.ToString(CultureInfo.InvariantCulture));
            configuration[key].GetInt64().ShouldBe(maximum); commits.ShouldBe(1);
            await input.ValueChanged.InvokeAsync(minimum.ToString(CultureInfo.InvariantCulture));
            configuration[key].GetInt64().ShouldBe(minimum); commits.ShouldBe(2);
            configuration.ContainsKey(property).ShouldBeFalse();
            configuration["Future"].GetProperty("Keep").GetBoolean().ShouldBeTrue();
        });
    }
}
