using System.Text.Json;
using FluxFlow.Json.Model;
using FluxFlow.Components.Expectations.Contracts;
using FluxFlow.Components.Expectations.Diagnostics;
using FluxFlow.Components.Expectations.Nodes;
using FluxFlow.Components.Expectations.Options;
using FluxFlow.Components.Projections.Contracts;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.DependencyInjection;
using FluxFlow.Engine;
using FluxFlow.Data;
using FluxFlow.Engine.Hosting;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using FluxFlow.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;
using static FluxFlow.Testing.CanonicalTestApplication;

namespace FluxFlow.Components.Expectations.Composition.Tests;

public sealed class ExpectationsServiceCollectionExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly ApplicationAddress Input = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        ExpectationsComponentDefinition.Ports.Input);
    private static readonly ApplicationAddress Output = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        ExpectationsComponentDefinition.Ports.Output);
    private static readonly ApplicationAddress Events = ApplicationAddress.WorkflowPort(
        "main",
        "node",
        "Events");

    [Fact]
    public async Task AddExpectations_registers_only_the_canonical_factory()
    {
        var services = new ServiceCollection().AddExpectations();

        await using var provider = services.BuildServiceProvider();
        var type = ExpectationsComponentDefinition.Types.EventExpectation;
        var instance = await FluxFlowRegistrationExtensions.CreateFactoryMap(provider)[type](
            new ComponentActivationContext(provider, "main", "node", new ComponentDefinition(type, [])));
        try
        {
            instance.Inputs.Keys.ShouldBe(new[] { "Input" });
            instance.Outputs.Keys.ShouldBe(new[] { "Output", "Events" });
            instance.InputMetadata.Values.ShouldAllBe(p => p.MessageType == typeof(FlowValue) && p.Kind == ComponentPortKind.Message);
            instance.OutputMetadata.Values.ShouldAllBe(p => p.MessageType == typeof(FlowValue));
        }
        finally { await instance.DisposeAsync(); }
    }

    [Fact]
    public void AddExpectations_is_idempotent()
    {
        using var provider = new ServiceCollection().AddExpectations().AddExpectations().BuildServiceProvider();
        var catalog = FluxFlowRegistrationExtensions.CreateFactoryMap(provider);
        catalog.Keys.ShouldBe([ExpectationsComponentDefinition.Types.EventExpectation]);
    }

    [Fact]
    public async Task Canonical_host_matches_events_preserves_lineage_and_uses_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);

        await WithNodeAsync(
            async (ports, _) =>
            {
                var resultReceive = ports.ReceiveAsync<EventExpectationResult>(
                    Output,
                    Timeout);
                var ignored = FlowMessage.Create(CreateEvent(
                    timestamp.AddSeconds(-1),
                    "operation.completed",
                    subject: "orders/1",
                    status: "failed",
                    attributes: new Dictionary<string, string>
                    {
                        ["tenant"] = "south"
                    }));
                var matched = FlowMessage.Create(
                    CreateEvent(
                        timestamp,
                        "operation.completed",
                        subject: "orders/2",
                        status: "failed",
                        payloadPreview: "abcdef",
                        attributes: new Dictionary<string, string>
                        {
                            ["tenant"] = "north"
                        }),
                    new CorrelationId("matched"),
                    new TraceId("trace-matched"));

                (await ports.SendAsync(Input, ignored)).IsAccepted.ShouldBeTrue();
                (await ports.SendAsync(Input, matched)).IsAccepted.ShouldBeTrue();

                var result = (await resultReceive).Message.ShouldNotBeNull();
                result.IsError.ShouldBeFalse();
                result.CorrelationId.ShouldBe(matched.CorrelationId);
                result.TraceId.ShouldBe(matched.TraceId);
                result.CausationId.ShouldBe(matched.MessageId);
                var value = result.Value;
                value.EvaluatedAt.ShouldBe(timestamp);
                value.Name.ShouldBe("failed-order");
                value.Kind.ShouldBe(EventExpectationResultKind.Expect);
                value.Satisfied.ShouldBeTrue();
                value.Matched.ShouldBeTrue();
                value.TimedOut.ShouldBeFalse();
                value.MatchedEvent.ShouldNotBeNull().Subject.ShouldBe("orders/2");
                value.MatchedEvent.PayloadPreview.ShouldBe("abcd");
                value.ObservedEvents.Count.ShouldBe(2);
            },
            Properties(
                ("name", "failed-order"),
                ("maxObservedEvents", 2),
                ("maxPreviewChars", 4),
                ("filter", new EventFilter
                {
                    Type = "operation.completed",
                    SubjectPrefix = "orders/",
                    Status = "failed",
                    Attributes = new Dictionary<string, string>
                    {
                        ["tenant"] = "north"
                    }
                })),
            clock);
    }

    [Fact]
    public async Task Canonical_host_binds_nested_filter_configuration()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:30:00Z");

        await WithNodeAsync(
            async (ports, _) =>
            {
                var resultReceive = ports.ReceiveAsync<EventExpectationResult>(
                    Output,
                    Timeout);
                var message = FlowMessage.Create(CreateEvent(
                    timestamp,
                    "task.completed",
                    source: "worker",
                    subject: "jobs/42",
                    status: "failed",
                    attributes: new Dictionary<string, string>
                    {
                        ["tenant"] = "north"
                    }));

                (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();
                var value = (await resultReceive).Message.ShouldNotBeNull()
                    .Value;
                value.Filter.TypePrefix.ShouldBe("task.");
                value.Filter.Status.ShouldBe("failed");
                value.Filter.SubjectPrefix.ShouldBe("jobs/");
                value.Filter.Attributes["tenant"].ShouldBe("north");
                value.Satisfied.ShouldBeTrue();
            },
            Properties(("filter", new EventFilter
            {
                TypePrefix = "task.",
                SubjectPrefix = "jobs/",
                Status = "failed",
                Attributes = new Dictionary<string, string>
                {
                    ["tenant"] = "north"
                }
            })));
    }

    [Fact]
    public async Task Canonical_host_uses_keyed_clock_for_timeout_result()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T13:00:00Z");
        var clock = new FakeTimeProvider(timestamp);

        await WithNodeAsync(
            async (ports, _) =>
            {
                var resultReceive = ports.ReceiveAsync<EventExpectationResult>(
                    Output,
                    Timeout);
                clock.Advance(TimeSpan.FromMilliseconds(500));

                var result = (await resultReceive).Message.ShouldNotBeNull();
                result.IsError.ShouldBeFalse();
                var value = result.Value;
                value.Kind.ShouldBe(EventExpectationResultKind.Guard);
                value.Satisfied.ShouldBeTrue();
                value.Matched.ShouldBeFalse();
                value.TimedOut.ShouldBeTrue();
                value.EvaluatedAt.ShouldBe(clock.GetUtcNow());
            },
            Properties(
                ("kind", EventExpectationNodeKind.Guard),
                ("timeoutMilliseconds", 500),
                ("filter", new EventFilter { Status = "failed" })),
            clock);
    }

    [Fact]
    public async Task Canonical_host_exposes_correlated_events()
    {
        await WithNodeAsync(
            async (ports, _) =>
            {
                var eventReceive = ports.ReceiveAsync<global::FluxFlow.Data.FlowValue>(
                    Events,
                    Timeout);
                var message = FlowMessage.Create(CreateEvent(
                    DateTimeOffset.Parse("2026-06-18T13:30:00Z"),
                    "job.finished"));

                (await ports.SendAsync(Input, message)).IsAccepted.ShouldBeTrue();

                var @event = (await eventReceive).Message.ShouldNotBeNull();
                @event.CorrelationId.ShouldBe(message.CorrelationId);
                @event.ToFlowEvent().Name.ShouldBe(ExpectationDiagnosticNames.Matched);
                @event.ToFlowEvent().Attributes["satisfied"].ShouldBe(true);
                @event.ToFlowEvent().Attributes["isError"].ShouldBe(false);
            },
            Properties(("filter", new EventFilter { Type = "job.finished" })));
    }

    [Fact]
    public async Task Canonical_host_rejects_obsolete_component_type()
    {
        await using var host = await StartHostAsync(
            Properties(("filter", new EventFilter { Type = "match" })),
            componentType: "event.expectation");

        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Diagnostics.ShouldContain(failure =>
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!.Contains(
                "unknown type 'event.expectation'",
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("timeoutMilliseconds", 0, "timeoutMilliseconds")]
    [InlineData("maxObservedEvents", -1, "maxObservedEvents")]
    [InlineData("maxPreviewChars", -1, "maxPreviewChars")]
    [InlineData("boundedCapacity", 0, "capacity")]
    public async Task Invalid_configuration_surfaces_preparation_failure(
        string optionName,
        object value,
        string expectedMessage)
    {
        await using var host = await StartHostAsync(Properties((optionName, value)));

        AssertPreparationFailure(host, expectedMessage);
    }

    private static async Task WithNodeAsync(
        Func<ApplicationPorts, CanonicalApplicationTestHost, Task> run,
        IReadOnlyDictionary<string, object?> properties,
        TimeProvider? clock = null)
    {
        await using var host = await StartHostAsync(properties, clock);
        host.StartResult.Succeeded.ShouldBeTrue();

        await run(host.GetRequiredPorts(), host);
    }

    private static ValueTask<CanonicalApplicationTestHost> StartHostAsync(
        IReadOnlyDictionary<string, object?> properties,
        TimeProvider? clock = null,
        string componentType = ExpectationsComponentDefinition.Types.EventExpectation)
    {
        var componentProperties = properties.ToDictionary(
            static property => property.Key,
            static property => property.Value,
            StringComparer.Ordinal);
        IReadOnlyList<string>? resources = null;
        if (clock is not null)
        {
            componentProperties[ExpectationsComponentDefinition.Resources.Clock] = "Resources.clock";
            resources = ["clock"];
        }

        return CanonicalApplicationTestHost.StartAsync(
            SingleComponent(componentType, componentProperties, resources, discardUnmatched: ["Output", "Events"]),
            registry => registry.AddExpectations(),
            registerResources: context =>
            {
                if (clock is not null)
                {
                    context.Services.AddExternalFluxFlowResource<TimeProvider>(
                        ApplicationAddress.Resource("clock"),
                        clock);
                }
            });
    }

    private static void AssertPreparationFailure(
        CanonicalApplicationTestHost host,
        string expectedMessage)
    {
        host.StartResult.Succeeded.ShouldBeFalse();
        host.StartResult.Update!.Status.ShouldBe(ApplicationUpdateStatus.Rejected);
        host.StartResult.Update.Diagnostics.ShouldContain(failure =>
            failure.Stage == ApplicationUpdateStage.ComponentPreparation &&
            failure.Error.Details!.Value.GetProperty("exceptionMessage").GetString()!.Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
        host.RuntimeAccess.Ports.ShouldBeNull();
    }

    private static ProjectionEvent CreateEvent(
        DateTimeOffset timestamp,
        string type,
        string source = "processor",
        string? subject = null,
        string? status = null,
        string? channel = null,
        string? payloadPreview = null,
        string? sourceNodeId = null,
        IReadOnlyDictionary<string, string>? attributes = null)
        => new()
        {
            Timestamp = timestamp,
            Type = type,
            Source = source,
            SourceNodeId = sourceNodeId,
            Subject = subject,
            Status = status,
            Channel = channel,
            PayloadBytes = payloadPreview?.Length,
            PayloadPreview = payloadPreview,
            Attributes = attributes ?? new Dictionary<string, string>(StringComparer.Ordinal)
        };

}
