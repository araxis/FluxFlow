using System.Text.Json;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Resilience.Composition.Tests;

public sealed class ResilienceServiceCollectionExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input =
        ApplicationAddress.WorkflowPort("main", "node", ResilienceComponentDefinition.Ports.Input);
    private static readonly ApplicationAddress Ack =
        ApplicationAddress.WorkflowPort("main", "node", ResilienceComponentDefinition.Ports.Ack);
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("main", "node", ResilienceComponentDefinition.Ports.Output);

    [Fact]
    public void Registration_declares_message_and_signal_ports()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddResilienceComponents());
        var registration = registry.Components[ResilienceComponentDefinition.Types.Retry];

        registration.Inputs[ResilienceComponentDefinition.Ports.Input].MessageType.ShouldBe(typeof(JsonElement));
        AssertSignal(registration, ResilienceComponentDefinition.Ports.Ack);
        AssertSignal(registration, ResilienceComponentDefinition.Ports.Nak);
        AssertSignal(registration, ResilienceComponentDefinition.Ports.Cancel);
        registration.Outputs[ResilienceComponentDefinition.Ports.Output].MessageType
            .ShouldBe(typeof(RetrySignal<JsonElement>));
    }

    [Fact]
    public void Designer_metadata_is_valid_and_describes_signal_ports()
    {
        var metadata = Metadata();

        metadata.Type.Value.ShouldBe(ResilienceComponentDefinition.Types.Retry);
        metadata.DisplayName?.Value.ShouldBe("Flow Retry");
        metadata.Category.ShouldBe(new ComponentCategory("Resilience"));
        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Ports.Select(port => port.Name.Value).ShouldBe(
            ["Input", "Ack", "Nak", "Cancel", "Output"],
            ignoreOrder: false);
        foreach (var name in new[] { "Ack", "Nak", "Cancel" })
        {
            var port = metadata.Ports.Single(item => item.Name.Value == name);
            port.Direction.ShouldBe(PortDirection.Input);
            AttributeValue(port.Attributes, PortDesignMetadataAttributeNames.Kind)
                .ShouldBe(PortDesignMetadataAttributeValues.Signal);
        }
        metadata.Ports.Single(port => port.Name.Value == "Output")
            .ValueType?.Value.ShouldBe("RetrySignal<JsonElement>");
    }

    [Fact]
    public void Designer_metadata_describes_flat_retry_options_and_hints()
    {
        var options = Metadata().Options.ToDictionary(option => option.Name.Value, StringComparer.Ordinal);
        options.Keys.ShouldBe(
        [
            "name",
            "strategy",
            "initialDelayMilliseconds",
            "incrementMilliseconds",
            "maximumDelayMilliseconds",
            "maximumAttempts",
            "maximumDurationMilliseconds",
            "jitterFactor",
            "attemptTimeoutMilliseconds",
            "capacity"
        ], ignoreOrder: false);

        AssertHints(options["strategy"], "Retry", OptionDesignMetadataAttributeValues.Primary);
        AssertHints(options["initialDelayMilliseconds"], "Timing", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Number);
        AssertHints(options["maximumAttempts"], "Limits", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Number);
        AssertHints(options["attemptTimeoutMilliseconds"], "Timeouts", OptionDesignMetadataAttributeValues.Primary, OptionDesignMetadataAttributeValues.Number);
        AssertHints(options["capacity"], "Runtime", OptionDesignMetadataAttributeValues.Advanced, OptionDesignMetadataAttributeValues.Number);
        options["maximumAttempts"].DefaultValue.ShouldBe(new FlowRetryOptions().MaximumAttempts);
        options["jitterFactor"].Min.ShouldBe(0);
        options["jitterFactor"].Max.ShouldBe(1);
    }

    [Fact]
    public void Designer_metadata_describes_host_owned_resources()
    {
        var resources = Metadata().Resources.ToDictionary(resource => resource.Name.Value, StringComparer.Ordinal);
        resources.Keys.ShouldBe(["Clock", "Jitter"], ignoreOrder: false);
        AssertResource(resources["Clock"], ResourceDesignMetadataAttributeValues.Clock);
        AssertResource(resources["Jitter"], ResourceDesignMetadataAttributeValues.Delegate);
    }

    [Fact]
    public async Task Hosted_retry_accepts_ack_through_signal_port()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                ResilienceComponentDefinition.Types.Retry,
                Properties(
                    ("strategy", "Fixed"),
                    ("initialDelayMilliseconds", 0),
                    ("maximumAttempts", 2),
                    ("attemptTimeoutMilliseconds", 10_000))),
            registry => registry.AddResilienceComponents());
        host.StartResult.Succeeded.ShouldBeTrue(string.Join(
            Environment.NewLine,
            host.StartResult.Update?.Diagnostics.Select(failure =>
                $"{failure.Stage}: {failure.Error.Message} {failure.Error.Details}") ?? []));
        var ports = host.GetRequiredPorts();
        var message = FlowMessage.Create(JsonSerializer.SerializeToElement("payload"));

        var attemptReceive = ports.ReceiveAsync<RetrySignal<JsonElement>>(Output, Timeout);
        (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();
        var attempt = (await attemptReceive).Message.ShouldNotBeNull();
        attempt.Value.Status.ShouldBe(RetrySignalStatus.Attempt);

        var completedReceive = ports.ReceiveAsync<RetrySignal<JsonElement>>(Output, Timeout);
        (await ports.SendAsync(Ack, attempt.With("ack"))).IsAccepted.ShouldBeTrue();
        var completed = (await completedReceive).Message.ShouldNotBeNull();
        completed.Value.Status.ShouldBe(RetrySignalStatus.Completed);
        completed.TraceId.ShouldBe(message.TraceId);
    }

    [Fact]
    public async Task Invalid_configuration_is_rejected_during_preparation()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                ResilienceComponentDefinition.Types.Retry,
                Properties(("capacity", 0))),
            registry => registry.AddResilienceComponents());

        host.StartResult.Succeeded.ShouldBeFalse();
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private static ComponentDesignMetadata Metadata()
        => ResilienceComponentDefinition.CreateMetadata().ShouldHaveSingleItem();

    private static void AssertSignal(ComponentDescriptor registration, string name)
    {
        registration.Inputs[name].Kind.ShouldBe(ComponentPortKind.Signal);
        registration.Inputs[name].MessageType.ShouldBe(typeof(object));
    }

    private static void AssertHints(
        OptionDesignMetadata option,
        string section,
        string importance,
        string? editor = null)
    {
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Section).ShouldBe(section);
        AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Importance).ShouldBe(importance);
        if (editor is null)
        {
            option.Attributes.ContainsKey(new ComponentAttributeName(OptionDesignMetadataAttributeNames.Editor))
                .ShouldBeFalse();
        }
        else
        {
            AttributeValue(option.Attributes, OptionDesignMetadataAttributeNames.Editor).ShouldBe(editor);
        }
    }

    private static void AssertResource(ResourceDesignMetadata resource, string pickerKind)
    {
        resource.IsRequired.ShouldBeFalse();
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.Ownership)
            .ShouldBe(ResourceDesignMetadataAttributeValues.HostOwned);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.PickerKind)
            .ShouldBe(pickerKind);
        AttributeValue(resource.Attributes, ResourceDesignMetadataAttributeNames.KeyPattern)
            .ShouldBe("Resources.{name}");
    }

    private static string AttributeValue(
        IReadOnlyDictionary<ComponentAttributeName, ComponentAttributeValue> attributes,
        string name)
        => attributes[new ComponentAttributeName(name)].Value;
}
