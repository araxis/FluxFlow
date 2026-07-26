using FluxFlow.Components.Mqtt.Acknowledgements;
using FluxFlow.Components.Mqtt.Client;
using FluxFlow.Components.Mqtt.Configuration;
using FluxFlow.Components.Mqtt.Contracts;
using FluxFlow.Components.Mqtt.Events;
using FluxFlow.Components.Mqtt.Nodes;
using FluxFlow.Components.Mqtt.Options;
using FluxFlow.Components.Mqtt.Subscriptions;
using FluxFlow.Components.Mqtt.Transport;
using FluxFlow.Data;
using FluxFlow.Nodes;
using FluxFlow.Resilience;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using Xunit;

namespace FluxFlow.Components.Mqtt.Tests;

public sealed class MqttClientControllerTests
{
    [Fact]
    public void ConfigurationRejectsKeepAliveBeyondProtocolLimit()
    {
        var configuration = Configuration(
            "client-1",
            new MqttBrokerConfiguration { Host = "broker.internal" }) with
        {
            KeepAlive = TimeSpan.FromSeconds(ushort.MaxValue + 1d)
        };

        Should.Throw<ArgumentOutOfRangeException>(() =>
            new MqttClientController(configuration, new VNextRecordingMqttTransportFactory()));
    }

    [Fact]
    public async Task DisconnectedCommandsReturnNormalTransientResultsAndLifecycleIsIdempotent()
    {
        var session = new VNextRecordingMqttTransportSession();
        await using var controller = CreateController(session, autoConnect: false);
        await controller.StartAsync();

        var disconnected = await controller.ExecuteAsync(new MqttPublishClientRequest
        {
            Message = Publish("events/one")
        });
        disconnected.ShouldBeOfType<MqttClientFailureResult>();
        disconnected.IsError.ShouldBeTrue();
        disconnected.Error!.Code.ShouldBe(MqttClientErrorCodes.NotConnected);
        disconnected.Error.IsTransient.ShouldBeTrue();

        (await controller.ExecuteAsync(new MqttConnectRequest()))
            .ShouldBeOfType<MqttConnectResult>().Changed.ShouldBeTrue();
        (await controller.ExecuteAsync(new MqttConnectRequest()))
            .ShouldBeOfType<MqttConnectResult>().Changed.ShouldBeFalse();
        session.ConnectCalls.ShouldBe(1);

        (await controller.ExecuteAsync(new MqttDisconnectRequest()))
            .ShouldBeOfType<MqttDisconnectResult>().Changed.ShouldBeTrue();
        (await controller.ExecuteAsync(new MqttDisconnectRequest()))
            .ShouldBeOfType<MqttDisconnectResult>().Changed.ShouldBeFalse();
        session.DisconnectCalls.ShouldBe(1);
    }

    [Fact]
    public async Task InvalidPublishCommandReturnsNonTransientResultWithoutCallingTransport()
    {
        var session = new VNextRecordingMqttTransportSession();
        await using var controller = CreateController(session);
        await controller.StartAsync();

        var result = await controller.ExecuteAsync(new MqttPublishClientRequest
        {
            Message = Publish("invalid/#")
        });

        result.ShouldBeOfType<MqttClientFailureResult>();
        result.Error!.Code.ShouldBe(MqttClientErrorCodes.InvalidRequest);
        result.Error.IsTransient.ShouldBeFalse();
        session.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task TwoLogicalClientsCanShareBrokerSettingsWithoutSharingSessions()
    {
        var factory = new VNextRecordingMqttTransportFactory();
        var broker = new MqttBrokerConfiguration { Host = "broker.internal" };
        await using var first = new MqttClientController(Configuration("client-1", broker), factory);
        await using var second = new MqttClientController(Configuration("client-2", broker), factory);

        await first.StartAsync();
        await second.StartAsync();

        factory.Sessions.Count.ShouldBe(2);
        first.IsConnected.ShouldBeTrue();
        second.IsConnected.ShouldBeTrue();
    }

    [Fact]
    public async Task ControlNodePreservesInputOrderWhilePublishingConcurrently()
    {
        var session = new VNextRecordingMqttTransportSession
        {
            PublishHandler = async (message, cancellationToken) =>
            {
                await Task.Delay(
                    message.Topic.EndsWith("first", StringComparison.Ordinal) ? 80 : 1,
                    cancellationToken);
            }
        };
        await using var controller = CreateController(session);
        await controller.StartAsync();
        await using var node = new MqttControlNode(controller, new MqttControlOptions
        {
            RequestProcessing = MqttRequestProcessing.Concurrent,
            ResultOrder = MqttResultOrder.PreserveInput,
            MaximumConcurrentRequests = 2,
            MaximumPendingRequests = 8
        });
        var output = MqttTestContext.Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create<MqttClientRequest>(new MqttPublishClientRequest
        {
            Message = Publish("events/first")
        }));
        await node.Input.SendAsync(FlowMessage.Create<MqttClientRequest>(new MqttPublishClientRequest
        {
            Message = Publish("events/second")
        }));

        var first = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload
            .ShouldBeOfType<MqttPublishOperationResult>();
        var second = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload
            .ShouldBeOfType<MqttPublishOperationResult>();
        first.Topic.ShouldBe("events/first");
        second.Topic.ShouldBe("events/second");
        session.Published.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ControlNodeCanEmitConcurrentResultsInCompletionOrder()
    {
        var session = new VNextRecordingMqttTransportSession
        {
            PublishHandler = async (message, cancellationToken) =>
            {
                await Task.Delay(
                    message.Topic.EndsWith("first", StringComparison.Ordinal) ? 80 : 1,
                    cancellationToken);
            }
        };
        await using var controller = CreateController(session);
        await controller.StartAsync();
        await using var node = new MqttControlNode(controller, new MqttControlOptions
        {
            RequestProcessing = MqttRequestProcessing.Concurrent,
            ResultOrder = MqttResultOrder.Completion,
            MaximumConcurrentRequests = 2,
            MaximumPendingRequests = 8
        });
        var output = MqttTestContext.Sink(node.Output);

        await node.Input.SendAsync(FlowMessage.Create<MqttClientRequest>(new MqttPublishClientRequest
        {
            Message = Publish("events/first")
        }));
        await node.Input.SendAsync(FlowMessage.Create<MqttClientRequest>(new MqttPublishClientRequest
        {
            Message = Publish("events/second")
        }));

        var firstResult = (await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5))).Payload
            .ShouldBeOfType<MqttPublishOperationResult>();
        firstResult.Topic.ShouldBe("events/second");
    }

    [Fact]
    public async Task AutoConnectAvailabilityFailureDoesNotFailStartAndRestoresSubscriptions()
    {
        var session = new VNextRecordingMqttTransportSession
        {
            ConnectFailuresRemaining = 1
        };
        await using var controller = new MqttClientController(
            new MqttClientConfiguration
            {
                Name = "client-1",
                ClientId = "client-1",
                Broker = new MqttBrokerConfiguration { Host = "broker.internal" },
                AutoConnect = MqttAutoConnectMode.OnStart,
                Reconnect = new MqttReconnectConfiguration
                {
                    Enabled = true,
                    Policy = new MqttRetryPolicy
                    {
                        Strategy = MqttRetryStrategy.Fixed,
                        InitialDelay = TimeSpan.FromMilliseconds(1),
                        MaximumDelay = TimeSpan.FromMilliseconds(1),
                        JitterFactor = 0,
                        MaximumAttempts = 3
                    }
                },
                Subscriptions = new Dictionary<string, MqttSubscriptionDefinition>(StringComparer.Ordinal)
                {
                    ["commands"] = new() { TopicFilter = "commands/+" }
                }
            },
            new VNextRecordingMqttTransportFactory(() => session));

        await controller.StartAsync();

        await WaitUntilAsync(() => controller.IsConnected);
        session.ConnectCalls.ShouldBe(2);
        session.Subscribed.ShouldContain(item =>
            item.Identity == "name:commands" && item.Subscription.TopicFilter == "commands/+");
    }

    [Fact]
    public async Task ReconnectUsesInjectedJitterSourceAndSharedRetrySchedule()
    {
        var session = new VNextRecordingMqttTransportSession
        {
            ConnectFailuresRemaining = 1
        };
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var controller = new MqttClientController(
            new MqttClientConfiguration
            {
                Name = "client-1",
                ClientId = "client-1",
                Broker = new MqttBrokerConfiguration { Host = "broker.internal" },
                AutoConnect = MqttAutoConnectMode.OnStart,
                Reconnect = new MqttReconnectConfiguration
                {
                    Enabled = true,
                    Policy = new MqttRetryPolicy
                    {
                        Strategy = MqttRetryStrategy.Fixed,
                        InitialDelay = TimeSpan.FromSeconds(10),
                        MaximumDelay = TimeSpan.FromSeconds(20),
                        JitterFactor = 0.5,
                        MaximumAttempts = 1
                    }
                }
            },
            new VNextRecordingMqttTransportFactory(() => session),
            clock,
            new FixedJitterSource(1));
        await using var events = await controller.SubscribeEventsAsync(8);

        await controller.StartAsync();
        var scheduled = await ReadEventAsync<MqttReconnectScheduledEvent>(events.Events);

        scheduled.Attempt.ShouldBe(1);
        scheduled.Delay.ShouldBe(TimeSpan.FromSeconds(15));
        clock.Advance(scheduled.Delay);
        await WaitUntilAsync(() => controller.IsConnected);
        session.ConnectCalls.ShouldBe(2);
    }

    [Fact]
    public async Task FailedSubscriptionRestorationLeavesAutoConnectInactive()
    {
        var session = new VNextRecordingMqttTransportSession
        {
            SubscribeHandler = (_, _, _) => ValueTask.FromException(
                new InvalidOperationException("Subscription rejected."))
        };
        await using var controller = CreateController(
            session,
            subscriptions: new Dictionary<string, MqttSubscriptionDefinition>(StringComparer.Ordinal)
            {
                ["commands"] = new() { TopicFilter = "commands/+" }
            });

        await controller.StartAsync();

        controller.IsConnected.ShouldBeFalse();
        session.ConnectCalls.ShouldBe(1);
        session.DisconnectCalls.ShouldBe(1);
    }

    [Fact]
    public async Task ExplicitDisconnectSuppressesReconnectUntilConnect()
    {
        var session = new VNextRecordingMqttTransportSession();
        await using var controller = new MqttClientController(
            new MqttClientConfiguration
            {
                Name = "client-1",
                ClientId = "client-1",
                Broker = new MqttBrokerConfiguration { Host = "broker.internal" },
                AutoConnect = MqttAutoConnectMode.OnStart,
                Reconnect = new MqttReconnectConfiguration
                {
                    Enabled = true,
                    Policy = new MqttRetryPolicy
                    {
                        InitialDelay = TimeSpan.FromMilliseconds(1),
                        MaximumDelay = TimeSpan.FromMilliseconds(1),
                        JitterFactor = 0
                    }
                }
            },
            new VNextRecordingMqttTransportFactory(() => session));
        await controller.StartAsync();

        await controller.ExecuteAsync(new MqttDisconnectRequest());
        await session.EmitDisconnectedAsync();
        await Task.Delay(50);

        session.ConnectCalls.ShouldBe(1);
        var status = (await controller.ExecuteAsync(new MqttStatusRequest()))
            .ShouldBeOfType<MqttStatusResult>();
        status.Status.ReconnectSuppressed.ShouldBeTrue();

        (await controller.ExecuteAsync(new MqttConnectRequest()))
            .ShouldBeOfType<MqttConnectResult>().Changed.ShouldBeTrue();
        session.ConnectCalls.ShouldBe(2);
    }

    [Fact]
    public async Task NonTransientTransportFailureDoesNotScheduleReconnect()
    {
        var session = new VNextRecordingMqttTransportSession
        {
            ConnectFailuresRemaining = 1,
            ConnectFailure = () => new MqttTransportException(
                "Credentials rejected.",
                "Authentication",
                isTransient: false)
        };
        await using var controller = new MqttClientController(
            new MqttClientConfiguration
            {
                Name = "client-1",
                ClientId = "client-1",
                Broker = new MqttBrokerConfiguration { Host = "broker.internal" },
                Reconnect = new MqttReconnectConfiguration
                {
                    Enabled = true,
                    Policy = new MqttRetryPolicy
                    {
                        InitialDelay = TimeSpan.FromMilliseconds(1),
                        MaximumDelay = TimeSpan.FromMilliseconds(1),
                        JitterFactor = 0
                    }
                }
            },
            new VNextRecordingMqttTransportFactory(() => session));

        await controller.StartAsync();
        await Task.Delay(50);

        controller.IsConnected.ShouldBeFalse();
        session.ConnectCalls.ShouldBe(1);
    }

    [Fact]
    public async Task MissingNamedSubscriptionWaitsUntilControlAddsIt()
    {
        var session = new VNextRecordingMqttTransportSession();
        await using var controller = CreateController(session);
        await controller.StartAsync();
        await using var trigger = new MqttSubscriptionTriggerNode(
            controller,
            TriggerOptions(MqttSubscriptionTarget.Named("commands")));
        var output = MqttTestContext.Sink(trigger.Output);
        await trigger.StartAsync();
        await WaitUntilAsync(() => session.Subscribed.IsEmpty);

        await session.EmitAsync(Received("commands/one"));
        await Task.Delay(50);
        output.TryReceive(out _).ShouldBeFalse();

        var subscribe = await controller.ExecuteAsync(new MqttSubscribeRequest
        {
            Name = "commands",
            Subscription = new MqttSubscriptionDefinition
            {
                TopicFilter = "commands/+",
                Qos = MqttQos.AtLeastOnce
            }
        });
        subscribe.ShouldBeOfType<MqttSubscribeResult>().Changed.ShouldBeTrue();

        await session.EmitAsync(Received("commands/two"), "delivery-2");
        var received = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        received.Payload.Topic.ShouldBe("commands/two");
        received.Payload.MatchedSubscriptions.ShouldBe(["commands"]);
    }

    [Fact]
    public async Task TriggerClaimsAreExclusiveAndMixedMatchesEmitOnce()
    {
        var session = new VNextRecordingMqttTransportSession();
        await using var controller = CreateController(
            session,
            subscriptions: new Dictionary<string, MqttSubscriptionDefinition>(StringComparer.Ordinal)
            {
                ["commands"] = new() { TopicFilter = "commands/+" },
                ["commands-copy"] = new() { TopicFilter = "commands/+" }
            });
        await controller.StartAsync();
        var options = new MqttTriggerRegistrationOptions
        {
            TriggerId = "trigger-1",
            Subscriptions =
            [
                MqttSubscriptionTarget.Named("commands"),
                MqttSubscriptionTarget.FromInline(new MqttSubscriptionDefinition
                {
                    TopicFilter = "commands/#"
                })
            ]
        };
        await using var first = await controller.RegisterTriggerAsync(options);

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await controller.RegisterTriggerAsync(options with { TriggerId = "trigger-2" });
        });
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await controller.RegisterTriggerAsync(new MqttTriggerRegistrationOptions
            {
                TriggerId = "trigger-2",
                Subscriptions = [MqttSubscriptionTarget.Named("commands-copy")]
            });
        });

        await session.EmitAsync(Received("commands/start"));
        var delivery = await first.Messages.FirstAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        delivery.Message.MatchedSubscriptions.ShouldBe(["commands", "commands/#"]);
    }

    [Fact]
    public async Task DisposingTriggerRemovesInlineSubscriptionButKeepsNamedDesiredState()
    {
        var session = new VNextRecordingMqttTransportSession();
        await using var controller = CreateController(
            session,
            subscriptions: new Dictionary<string, MqttSubscriptionDefinition>(StringComparer.Ordinal)
            {
                ["commands"] = new() { TopicFilter = "commands/+" }
            });
        await controller.StartAsync();
        var registration = await controller.RegisterTriggerAsync(new MqttTriggerRegistrationOptions
        {
            TriggerId = "trigger-1",
            Subscriptions =
            [
                MqttSubscriptionTarget.Named("commands"),
                MqttSubscriptionTarget.FromInline(new MqttSubscriptionDefinition
                {
                    TopicFilter = "alerts/+"
                })
            ]
        });

        await registration.DisposeAsync();

        session.Unsubscribed.ShouldContain("filter:alerts/+");
        session.Unsubscribed.ShouldNotContain("name:commands");
        var status = (await controller.ExecuteAsync(new MqttStatusRequest()))
            .ShouldBeOfType<MqttStatusResult>();
        status.Status.DesiredSubscriptions.ShouldContain("name:commands");
        status.Status.DesiredSubscriptions.ShouldNotContain("filter:alerts/+");
    }

    [Fact]
    public async Task PartialInlineSubscriptionFailureRollsBackBrokerAndClaims()
    {
        var session = new VNextRecordingMqttTransportSession
        {
            SubscribeHandler = (identity, _, _) => identity == "filter:audit/+"
                ? ValueTask.FromException(new InvalidOperationException("Subscription rejected."))
                : ValueTask.CompletedTask
        };
        await using var controller = CreateController(session);
        await controller.StartAsync();
        var options = new MqttTriggerRegistrationOptions
        {
            TriggerId = "trigger-1",
            Subscriptions =
            [
                MqttSubscriptionTarget.FromInline(new MqttSubscriptionDefinition
                {
                    TopicFilter = "alerts/+"
                }),
                MqttSubscriptionTarget.FromInline(new MqttSubscriptionDefinition
                {
                    TopicFilter = "audit/+"
                })
            ]
        };

        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await controller.RegisterTriggerAsync(options);
        });

        session.Unsubscribed.ShouldContain("filter:alerts/+");
        await using var replacement = await controller.RegisterTriggerAsync(options with
        {
            TriggerId = "trigger-2",
            Subscriptions = [options.Subscriptions[0]]
        });
    }

    [Fact]
    public async Task AckSignalUsesTraceIdentityAndCompletesBrokerOutcomeOnce()
    {
        var session = new VNextRecordingMqttTransportSession();
        await using var controller = CreateController(session);
        await controller.StartAsync();
        await using var trigger = new MqttSubscriptionTriggerNode(
            controller,
            TriggerOptions(
                MqttSubscriptionTarget.FromInline(new MqttSubscriptionDefinition
                {
                    TopicFilter = "commands/+",
                    Qos = MqttQos.AtLeastOnce
                }),
                acknowledgement: true));
        var output = MqttTestContext.Sink(trigger.Output);
        var events = MqttTestContext.Sink(trigger.Events);
        await trigger.StartAsync();
        await WaitUntilAsync(() => !session.Subscribed.IsEmpty);

        await session.EmitAsync(Received("commands/run", MqttQos.AtLeastOnce));
        var received = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var signal = FlowMessage.Create(
            new { Ignored = true },
            traceId: received.TraceId);
        (await trigger.Ack.SendAsync(signal)).ShouldBeTrue();
        (await trigger.Nak.SendAsync(signal)).ShouldBeTrue();

        await WaitUntilAsync(() => session.Acknowledged.Count == 1);
        session.Acknowledged.Single().Outcome.ShouldBe(MqttWorkflowOutcome.Ack);
        await WaitUntilAsync(() => events.TryReceiveAll(out var values) &&
            values.Any(@event => @event.Name == "mqtt.receive.outcome-ignored"));
    }

    [Fact]
    public async Task WorkflowOutcomeTimeoutCompletesBrokerOutcomeOnceAndRejectsLateSignal()
    {
        var session = new VNextRecordingMqttTransportSession();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var controller = CreateController(session);
        await controller.StartAsync();
        await using var trigger = new MqttSubscriptionTriggerNode(
            controller,
            TriggerOptions(
                MqttSubscriptionTarget.FromInline(new MqttSubscriptionDefinition
                {
                    TopicFilter = "commands/+",
                    Qos = MqttQos.AtLeastOnce
                }),
                acknowledgement: true) with
            {
                OutcomeTimeout = TimeSpan.FromSeconds(1)
            },
            clock);
        var output = MqttTestContext.Sink(trigger.Output);
        var events = MqttTestContext.Sink(trigger.Events);
        await trigger.StartAsync();
        await WaitUntilAsync(() => !session.Subscribed.IsEmpty);

        await session.EmitAsync(Received("commands/run", MqttQos.AtLeastOnce));
        var received = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(1));

        await WaitUntilAsync(() => session.Acknowledged.Count == 1);
        session.Acknowledged.Single().Outcome.ShouldBe(MqttWorkflowOutcome.Timeout);
        (await trigger.Ack.SendAsync(FlowMessage.Create("late", traceId: received.TraceId))).ShouldBeTrue();
        await WaitUntilAsync(() => events.TryReceiveAll(out var values) &&
            values.Any(@event => @event.Name == "mqtt.receive.outcome-ignored"));
        session.Acknowledged.Count.ShouldBe(1);
    }

    [Fact]
    public async Task DeferredAcknowledgementRequiresAdapterCapability()
    {
        var session = new VNextRecordingMqttTransportSession
        {
            Capabilities = new()
        };
        await using var controller = CreateController(session);
        await controller.StartAsync();

        await Should.ThrowAsync<NotSupportedException>(async () =>
        {
            await controller.RegisterTriggerAsync(new MqttTriggerRegistrationOptions
            {
                TriggerId = "trigger-1",
                Subscriptions =
                [
                    MqttSubscriptionTarget.FromInline(new MqttSubscriptionDefinition
                    {
                        TopicFilter = "commands/+",
                        Qos = MqttQos.AtLeastOnce
                    })
                ],
                WorkflowAcknowledgement = MqttWorkflowAcknowledgement.Required,
                BrokerAcknowledgement = MqttBrokerAcknowledgement.AfterOutcome
            });
        });
    }

    [Fact]
    public async Task QosZeroDoesNotRequireOrInvokeBrokerAcknowledgement()
    {
        var session = new VNextRecordingMqttTransportSession
        {
            Capabilities = new()
        };
        await using var controller = CreateController(session);
        await controller.StartAsync();
        await using var trigger = new MqttSubscriptionTriggerNode(
            controller,
            TriggerOptions(
                MqttSubscriptionTarget.FromInline(new MqttSubscriptionDefinition
                {
                    TopicFilter = "commands/+",
                    Qos = MqttQos.AtMostOnce
                }),
                acknowledgement: true));
        var output = MqttTestContext.Sink(trigger.Output);
        await trigger.StartAsync();
        await WaitUntilAsync(() => !session.Subscribed.IsEmpty);

        await session.EmitAsync(Received("commands/run", MqttQos.AtMostOnce));
        var received = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await trigger.Nak.SendAsync(FlowMessage.Create("ignored", traceId: received.TraceId));

        await Task.Delay(20);
        session.Acknowledged.ShouldBeEmpty();
    }

    [Fact]
    public async Task OverlappingTriggersAggregateOneBrokerOutcomeAcrossPolicies()
    {
        var session = new VNextRecordingMqttTransportSession();
        await using var controller = CreateController(session);
        await controller.StartAsync();
        await using var automatic = new MqttSubscriptionTriggerNode(
            controller,
            new MqttSubscriptionTriggerOptions
            {
                TriggerId = "automatic",
                Subscriptions =
                [
                    MqttSubscriptionTarget.FromInline(new MqttSubscriptionDefinition
                    {
                        TopicFilter = "commands/#",
                        Qos = MqttQos.AtLeastOnce
                    })
                ],
                BrokerAcknowledgement = MqttBrokerAcknowledgement.Automatic
            });
        await using var deferred = new MqttSubscriptionTriggerNode(
            controller,
            new MqttSubscriptionTriggerOptions
            {
                TriggerId = "deferred",
                Subscriptions =
                [
                    MqttSubscriptionTarget.FromInline(new MqttSubscriptionDefinition
                    {
                        TopicFilter = "commands/+",
                        Qos = MqttQos.AtLeastOnce
                    })
                ],
                WorkflowAcknowledgement = MqttWorkflowAcknowledgement.Required,
                BrokerAcknowledgement = MqttBrokerAcknowledgement.AfterOutcome,
                OutcomeTimeout = TimeSpan.FromSeconds(5)
            });
        var automaticOutput = MqttTestContext.Sink(automatic.Output);
        var deferredOutput = MqttTestContext.Sink(deferred.Output);
        await automatic.StartAsync();
        await deferred.StartAsync();
        await WaitUntilAsync(() => session.Subscribed.Count == 2);

        await session.EmitAsync(
            Received("commands/run", MqttQos.AtLeastOnce),
            "shared-delivery");
        await automaticOutput.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var deferredMessage = await deferredOutput.ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(5));

        session.Acknowledged.ShouldBeEmpty();
        await deferred.Nak.SendAsync(FlowMessage.Create(
            "ignored",
            traceId: deferredMessage.TraceId));

        await WaitUntilAsync(() => session.Acknowledged.Count == 1);
        var acknowledgement = session.Acknowledged.Single();
        acknowledgement.Delivery.Value.ShouldBe("shared-delivery");
        acknowledgement.Outcome.ShouldBe(MqttWorkflowOutcome.Nak);
    }

    [Fact]
    public async Task TransportFailurePreservesNonTransientResultClassification()
    {
        var session = new VNextRecordingMqttTransportSession
        {
            PublishHandler = static (_, _) => ValueTask.FromException(
                new MqttTransportException(
                    "Publishing is not authorized.",
                    "Authentication",
                    isTransient: false))
        };
        await using var controller = CreateController(session);
        await controller.StartAsync();

        var result = await controller.ExecuteAsync(new MqttPublishClientRequest
        {
            Message = Publish("events/one")
        });

        result.ShouldBeOfType<MqttClientFailureResult>();
        result.Error!.IsTransient.ShouldBeFalse();
        result.Error.Code.ShouldBe(MqttClientErrorCodes.PublishFailed);
    }

    [Fact]
    public async Task PublishRequiresFlowContentWithExactBytes()
    {
        var session = new VNextRecordingMqttTransportSession();
        await using var controller = CreateController(session);
        await controller.StartAsync();

        var result = await controller.ExecuteAsync(new MqttPublishClientRequest
        {
            Message = new MqttPublishMessage
            {
                Topic = "events/one",
                Content = FlowContent.FromValue(FlowValue.From("not encoded"))
            }
        });

        result.ShouldBeOfType<MqttClientFailureResult>();
        result.Error!.Code.ShouldBe(MqttClientErrorCodes.InvalidRequest);
        result.Error.IsTransient.ShouldBeFalse();
        session.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task PublishNodePreservesLineageAndContinuesAfterInvalidInput()
    {
        var session = new VNextRecordingMqttTransportSession();
        await using var controller = CreateController(session);
        await controller.StartAsync();
        await using var node = new MqttPublishOperationNode(controller, maximumPendingRequests: 2);
        var output = MqttTestContext.Sink(node.Output);
        var correlationId = CorrelationId.New();
        var traceId = TraceId.New();
        var invalid = FlowMessage.Create(
            new MqttPublishMessage
            {
                Topic = "invalid/#",
                Content = FlowContent.FromBytes(new byte[] { 1 })
            },
            correlationId,
            traceId);

        (await node.Input.SendAsync(invalid)).ShouldBeTrue();
        var failure = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        failure.Payload.ShouldBeOfType<MqttClientFailureResult>();
        failure.Payload.Error!.Code.ShouldBe(MqttClientErrorCodes.InvalidRequest);
        failure.CorrelationId.ShouldBe(correlationId);
        failure.TraceId.ShouldBe(traceId);
        failure.CausationId.ShouldBe(invalid.MessageId);

        var valid = FlowMessage.Create(Publish("events/valid"), correlationId, traceId);
        (await node.Input.SendAsync(valid)).ShouldBeTrue();
        var success = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        success.Payload.ShouldBeOfType<MqttPublishOperationResult>();
        success.CorrelationId.ShouldBe(correlationId);
        success.TraceId.ShouldBe(traceId);
        success.CausationId.ShouldBe(valid.MessageId);
        session.Published.Count.ShouldBe(1);

        node.Complete();
        await node.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EventsNodeEmitsConnectionDomainEvents()
    {
        var session = new VNextRecordingMqttTransportSession();
        await using var controller = CreateController(session);
        await controller.StartAsync();
        await using var eventsNode = new MqttClientEventsNode(controller);
        var output = MqttTestContext.Sink(eventsNode.Output);
        await eventsNode.StartAsync();

        var @event = await output.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));
        @event.Payload.ShouldBeOfType<MqttClientConnectedEvent>();
        @event.Payload.Client.ShouldBe("client-1");
    }

    [Fact]
    public void RequestsAndResultsUseStableJsonDiscriminators()
    {
        MqttClientRequest request = new MqttConnectRequest();
        var requestJson = JsonSerializer.Serialize(request);
        requestJson.ShouldBe("{\"Operation\":\"Connect\"}");
        JsonSerializer.Deserialize<MqttClientRequest>(requestJson)
            .ShouldBeOfType<MqttConnectRequest>();

        MqttClientResult result = new MqttClientFailureResult(
            MqttClientOperation.Publish,
            new FluxFlow.Data.FlowError("mqtt.test", "Failed.", "Mqtt", true),
            DateTimeOffset.UnixEpoch);
        var resultJson = JsonSerializer.Serialize(result);
        resultJson.ShouldContain("\"Kind\":\"Error\"");
        resultJson.ShouldContain("\"IsError\":true");

        typeof(MqttControlNode).GetProperty("Errors").ShouldBeNull();
        typeof(MqttPublishOperationNode).GetProperty("Errors").ShouldBeNull();
        typeof(MqttSubscriptionTriggerNode).GetProperty("Errors").ShouldBeNull();
        typeof(MqttClientEventsNode).GetProperty("Errors").ShouldBeNull();
    }

    [Fact]
    public void TriggerSubscriptionsUseScalarOrMixedArrayJsonWithoutWrappers()
    {
        var single = new MqttSubscriptionTriggerOptions
        {
            TriggerId = "trigger-1",
            Subscriptions = [MqttSubscriptionTarget.Named("shared-alerts")]
        };
        var singleJson = JsonSerializer.Serialize(single);
        singleJson.ShouldContain("\"Subscription\":\"shared-alerts\"");
        singleJson.ShouldNotContain("\"Name\"");

        const string mixedJson = """
            {
              "TriggerId": "trigger-1",
              "Subscription": [
                "shared-alerts",
                {
                  "TopicFilter": "commands/+",
                  "Qos": "AtLeastOnce"
                }
              ]
            }
            """;
        var mixed = JsonSerializer.Deserialize<MqttSubscriptionTriggerOptions>(mixedJson);
        mixed.ShouldNotBeNull();
        mixed.Subscriptions.Count.ShouldBe(2);
        mixed.Subscriptions[0].Name.ShouldBe("shared-alerts");
        mixed.Subscriptions[1].Inline!.TopicFilter.ShouldBe("commands/+");
        mixed.Subscriptions[1].Inline!.Qos.ShouldBe(MqttQos.AtLeastOnce);
    }

    [Fact]
    public void RetryPolicySupportsFixedLinearAndExponentialSchedules()
    {
        new MqttRetryPolicy
        {
            Strategy = MqttRetryStrategy.Fixed,
            InitialDelay = TimeSpan.FromSeconds(2),
            JitterFactor = 0
        }.GetDelay(3).ShouldBe(TimeSpan.FromSeconds(2));
        new MqttRetryPolicy
        {
            Strategy = MqttRetryStrategy.Linear,
            InitialDelay = TimeSpan.FromSeconds(1),
            Increment = TimeSpan.FromSeconds(2),
            JitterFactor = 0
        }.GetDelay(3).ShouldBe(TimeSpan.FromSeconds(5));
        new MqttRetryPolicy
        {
            Strategy = MqttRetryStrategy.Exponential,
            InitialDelay = TimeSpan.FromSeconds(1),
            JitterFactor = 0
        }.GetDelay(4).ShouldBe(TimeSpan.FromSeconds(8));
    }

    private static MqttClientController CreateController(
        VNextRecordingMqttTransportSession session,
        bool autoConnect = true,
        IReadOnlyDictionary<string, MqttSubscriptionDefinition>? subscriptions = null)
        => new(
            Configuration(
                "client-1",
                new MqttBrokerConfiguration { Host = "broker.internal" },
                autoConnect,
                subscriptions),
            new VNextRecordingMqttTransportFactory(() => session));

    private static MqttClientConfiguration Configuration(
        string name,
        MqttBrokerConfiguration broker,
        bool autoConnect = true,
        IReadOnlyDictionary<string, MqttSubscriptionDefinition>? subscriptions = null)
        => new()
        {
            Name = name,
            ClientId = name,
            Broker = broker,
            AutoConnect = autoConnect ? MqttAutoConnectMode.OnStart : MqttAutoConnectMode.Disabled,
            Reconnect = new MqttReconnectConfiguration { Enabled = false },
            Subscriptions = subscriptions ??
                new Dictionary<string, MqttSubscriptionDefinition>(StringComparer.Ordinal)
        };

    private static MqttPublishMessage Publish(string topic)
        => new()
        {
            Topic = topic,
            Content = FlowContent.FromBytes(new byte[] { 1, 2, 3 }, "application/octet-stream")
        };

    private static MqttReceivedApplicationMessage Received(
        string topic,
        MqttQos qos = MqttQos.AtMostOnce)
        => new()
        {
            Timestamp = DateTimeOffset.UtcNow,
            Topic = topic,
            Content = FlowContent.FromBytes(new byte[] { 1, 2, 3 }, "application/octet-stream"),
            Qos = qos
        };

    private static MqttSubscriptionTriggerOptions TriggerOptions(
        MqttSubscriptionTarget target,
        bool acknowledgement = false)
        => new()
        {
            TriggerId = "trigger-1",
            Subscriptions = [target],
            WorkflowAcknowledgement = acknowledgement
                ? MqttWorkflowAcknowledgement.Required
                : MqttWorkflowAcknowledgement.None,
            BrokerAcknowledgement = acknowledgement
                ? MqttBrokerAcknowledgement.AfterOutcome
                : MqttBrokerAcknowledgement.Automatic,
            OutcomeTimeout = TimeSpan.FromSeconds(5),
            MaximumPendingMessages = 8
        };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= timeout)
                throw new TimeoutException("The expected MQTT test condition was not reached.");
            await Task.Delay(10);
        }
    }

    private static async Task<TEvent> ReadEventAsync<TEvent>(IAsyncEnumerable<MqttClientEvent> events)
        where TEvent : MqttClientEvent
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var @event in events.WithCancellation(timeout.Token))
        {
            if (@event is TEvent expected)
                return expected;
        }

        throw new InvalidOperationException($"MQTT event '{typeof(TEvent).Name}' was not emitted.");
    }

    private sealed class FixedJitterSource(double sample) : IRetryJitterSource
    {
        public double NextSample() => sample;
    }
}
