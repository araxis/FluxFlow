using System.Text.Json;
using FluxFlow.Components.Resilience.Designer;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Resilience.Contracts;
using FluxFlow.Components.Resilience.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Core.Addressing;
using FluxFlow.Data;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.Time.Testing;
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
    public void Registration_declares_canonical_message_ports()
    {
        var registry = ComponentCatalogTestHost.Create(
            services => services.AddFluxFlowComponents().AddResilience());
        var registration = registry.Components[ResilienceComponentDefinition.Types.Retry];

        registration.Inputs[ResilienceComponentDefinition.Ports.Input].MessageType.ShouldBe(typeof(FlowValue));
        AssertFeedback(registration, ResilienceComponentDefinition.Ports.Ack);
        AssertFeedback(registration, ResilienceComponentDefinition.Ports.Nak);
        AssertFeedback(registration, ResilienceComponentDefinition.Ports.Cancel);
        registration.Outputs[ResilienceComponentDefinition.Ports.Output].MessageType.ShouldBe(typeof(FlowValue));
    }

    [Fact]
    public void Designer_metadata_is_valid_and_describes_canonical_feedback_inputs()
    {
        var metadata = Metadata();

        metadata.Type.Value.ShouldBe(ResilienceComponentDefinition.Types.Retry);
        metadata.DisplayName?.Value.ShouldBe("Flow Retry");
        metadata.Category.ShouldBe(new ComponentCategory("Resilience"));
        ComponentDesignMetadataValidator.Validate(metadata).ShouldBeEmpty();
        metadata.Ports.Select(port => port.Name.Value).ShouldBe(
            ["Input", "Ack", "Nak", "Cancel", "Output", "Events"],
            ignoreOrder: false);
        foreach (var name in new[] { "Ack", "Nak", "Cancel" })
        {
            var port = metadata.Ports.Single(item => item.Name.Value == name);
            port.Direction.ShouldBe(PortDirection.Input);
            port.ValueType?.Value.ShouldBe(nameof(FlowValue));
            port.Attributes.ContainsKey(new ComponentAttributeName(PortDesignMetadataAttributeNames.Kind))
                .ShouldBeFalse();
            port.InputShape.ShouldNotBeNull().AcceptedKinds.ShouldBe(
                [JsonValueKind.Object, JsonValueKind.Array, JsonValueKind.String, JsonValueKind.Number,
                    JsonValueKind.True, JsonValueKind.False, JsonValueKind.Null]);
            port.InputShape.RequiredProperties.ShouldBeEmpty();
        }
        metadata.Ports.Single(port => port.Name.Value == "Output")
            .ValueType?.Value.ShouldBe(nameof(FlowValue));
    }

    [Fact]
    public void Designer_metadata_describes_flat_retry_options_and_hints()
    {
        var options = Metadata().Options.ToDictionary(option => option.Name.Value, StringComparer.Ordinal);
        options.Keys.ShouldBe(
        [
            "strategy",
            "initialDelayMilliseconds",
            "incrementMilliseconds",
            "maximumDelayMilliseconds",
            "maximumAttempts",
            "maximumDurationMilliseconds",
            "jitterFactor",
            "attemptTimeoutMilliseconds",
            "capacity",
            "processing"
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
        resources.Keys.ShouldBe(["Clock", "Jitter", "processing"], ignoreOrder: false);
        AssertResource(resources["Clock"], ResourceDesignMetadataAttributeValues.Clock);
        AssertResource(resources["Jitter"], ResourceDesignMetadataAttributeValues.Delegate);
    }

    [Fact]
    public async Task Hosted_retry_accepts_ack_through_canonical_input()
    {
        await using var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(
                ResilienceComponentDefinition.Types.Retry,
                Properties(
                    ("strategy", "Fixed"),
                    ("initialDelayMilliseconds", 0),
                    ("maximumAttempts", 2),
                    ("attemptTimeoutMilliseconds", 10_000)), discardUnmatched: ["Output", "Events"]),
            registry => registry.AddFluxFlowComponents().AddResilience());
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
            registry => registry.AddFluxFlowComponents().AddResilience());

        host.StartResult.Succeeded.ShouldBeFalse();
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hosted_retry_timer_failure_finishes_component_cleanup(bool disposalFails)
    {
        var clock = new CleanupClock { DisposalFails = disposalFails };
        var host = await CanonicalApplicationTestHost.StartAsync(
            SingleComponent(ResilienceComponentDefinition.Types.Retry,
                Properties(("Clock", "Resources.clock"), ("capacity", 4)), ["clock"], discardUnmatched: ["Output", "Events"]),
            registry => registry.AddFluxFlowComponents().AddResilience(),
            registerResources: context => context.Services.AddExternalFluxFlowResource<TimeProvider>(
                ApplicationAddress.Resource("clock"), clock));
        host.StartResult.Succeeded.ShouldBeTrue();
        var ports = host.GetRequiredPorts();
        foreach (var value in new[] { "first", "second" })
        {
            var message = FlowMessage.Create(JsonSerializer.SerializeToElement(value));
            var receive = ports.ReceiveAsync<RetrySignal<JsonElement>>(Output, Timeout);
            (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();
            var attempt = (await receive).Message.ShouldNotBeNull();
            attempt.TraceId.ShouldBe(message.TraceId);
            attempt.Value.Status.ShouldBe(RetrySignalStatus.Attempt);
            attempt.Value.Value.GetString().ShouldBe(value);
        }
        clock.StopFails = true;
        var error = await Should.ThrowAsync<AggregateException>(
            () => host.Application.StopAsync().AsTask().WaitAsync(Timeout));
        var failures = error.Flatten().InnerExceptions;
        failures.ShouldContain(item => ReferenceEquals(item, clock.StopFailure));
        if (disposalFails) failures.ShouldContain(item => ReferenceEquals(item, clock.DisposalFailure));
        failures.ShouldAllBe(item => ReferenceEquals(item, clock.StopFailure) ||
            disposalFails && ReferenceEquals(item, clock.DisposalFailure));
        host.Application.Current.ShouldBeNull();
        host.RuntimeAccess.Ports.ShouldBeNull();
        await host.Application.Events.Completion.WaitAsync(Timeout);
        await host.DisposeAsync();
        await host.DisposeAsync();
        clock.TimerDisposeCalls.ShouldBe(1);
        clock.DisposeCalls.ShouldBe(0);
    }

    private sealed class CleanupClock : TimeProvider, IDisposable
    {
        private readonly FakeTimeProvider _inner = new();
        public bool StopFails { get; set; }
        public bool DisposalFails { get; set; }
        public InvalidOperationException StopFailure { get; } = new("hosted retry timer stop failed");
        public InvalidOperationException DisposalFailure { get; } = new("hosted retry timer disposal failed");
        public int TimerDisposeCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public void Dispose() => DisposeCalls++;
        public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new Timer(_inner.CreateTimer(callback, state, dueTime, period), this);
        private sealed class Timer(ITimer inner, CleanupClock owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
                => owner.StopFails && dueTime == global::System.Threading.Timeout.InfiniteTimeSpan
                    ? throw owner.StopFailure : inner.Change(dueTime, period);
            public void Dispose() => throw new NotSupportedException();
            public async ValueTask DisposeAsync()
            {
                owner.TimerDisposeCalls++;
                await inner.DisposeAsync();
                if (owner.DisposalFails) throw owner.DisposalFailure;
            }
        }
    }

    private static ComponentDesignMetadata Metadata()
        => ComponentCatalogTestHost.CreateDesignMetadataCatalog(
                services => services.AddFluxFlowComponents().AddResilience().AddResilienceDesign()).All
            .ShouldHaveSingleItem();

    private static void AssertFeedback(ComponentDescriptor registration, string name)
    {
        registration.Inputs[name].Kind.ShouldBe(ComponentPortKind.Message);
        registration.Inputs[name].MessageType.ShouldBe(typeof(FlowValue));
        var shape = registration.Inputs[name].InputShape.ShouldNotBeNull();
        shape.Description.ShouldBe("Payload-independent feedback matched by trace and attempt headers.");
        shape.AcceptedKinds.ShouldBe(
            [JsonValueKind.Object, JsonValueKind.Array, JsonValueKind.String, JsonValueKind.Number,
                JsonValueKind.True, JsonValueKind.False, JsonValueKind.Null]);
        shape.RequiredProperties.ShouldBeEmpty();
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
