using System.Threading.Tasks.Dataflow;
using FluxFlow.Components.Designer;
using FluxFlow.Components.Designer.Contracts;
using FluxFlow.Components.Observability;
using FluxFlow.Components.Observability.Composition;
using FluxFlow.Components.Observability.Contracts;
using FluxFlow.Components.Observability.Options;
using FluxFlow.Composition;
using FluxFlow.Composition.Hosting;
using FluxFlow.Mapping;
using FluxFlow.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Xunit;

namespace FluxFlow.Components.Observability.Composition.Tests;

public sealed class ObservabilityCompositionNodeRegistryExtensionsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void RegisterObservabilityNodes_registers_closed_generic_metadata()
    {
        var registry = new CompositionNodeRegistry()
            .RegisterCounter<TestMessage>()
            .RegisterLogger<TestMessage>()
            .RegisterMetrics<TestMessage>();

        AssertMetadata<FlowCounterSnapshot>(
            registry,
            ObservabilityCompositionNodeTypes.Counter);
        AssertMetadata<FlowLogEntry>(
            registry,
            ObservabilityCompositionNodeTypes.Logger);
        AssertMetadata<FlowMetricSnapshot>(
            registry,
            ObservabilityCompositionNodeTypes.Metrics);
    }

    [Fact]
    public void Design_metadata_provider_returns_valid_observability_metadata()
    {
        var metadata = MetadataByType();

        metadata.Keys.ShouldBe([
            ObservabilityCompositionNodeTypes.Counter,
            ObservabilityCompositionNodeTypes.Logger,
            ObservabilityCompositionNodeTypes.Metrics
        ], ignoreOrder: false);

        foreach (var item in metadata.Values)
        {
            item.Category.ShouldBe(new ComponentCategory("Observability"));
            item.SuggestedEditorWidth.ShouldBe(460);
            ComponentDesignMetadataValidator.Validate(item).ShouldBeEmpty();
            item.Options.ShouldNotContain(option =>
                option.Name.Value == ObservabilityCompositionResourceNames.Clock ||
                option.Name.Value == ObservabilityCompositionResourceNames.ContextFactory ||
                option.Name.Value.StartsWith("attribute:", StringComparison.Ordinal));
        }

        AssertResources(
            metadata[ObservabilityCompositionNodeTypes.Counter],
            [
                (ObservabilityCompositionResourceNames.Engine, 0, false, nameof(IFlowExpressionEngine)),
                (ObservabilityCompositionResourceNames.ContextFactory, 1, false, "IFlowMapContextFactory<TInput>"),
                (ObservabilityCompositionResourceNames.Clock, 2, false, nameof(TimeProvider))
            ]);
        metadata[ObservabilityCompositionNodeTypes.Counter]
            .Resources
            .Single(resource => resource.Name.Value == ObservabilityCompositionResourceNames.Engine)
            .Attributes[new ComponentAttributeName("requiredWhenAnyOption")]
            .Value.ShouldBe("predicate,expression");

        AssertResources(
            metadata[ObservabilityCompositionNodeTypes.Logger],
            [
                (ObservabilityCompositionResourceNames.Clock, 0, false, nameof(TimeProvider)),
                (ObservabilityCompositionResourceNames.AttributeSelectorPrefix + "{name}", 1, false, "IObservabilityValueSelector<TInput>")
            ]);
        var attributeSelector = metadata[ObservabilityCompositionNodeTypes.Logger]
            .Resources
            .Single(resource => resource.Name.Value == ObservabilityCompositionResourceNames.AttributeSelectorPrefix + "{name}");
        attributeSelector.Attributes[new ComponentAttributeName("pattern")].Value.ShouldBe("true");
        attributeSelector.Attributes[new ComponentAttributeName("option")].Value.ShouldBe("attributeSelectors");

        AssertResources(
            metadata[ObservabilityCompositionNodeTypes.Metrics],
            [
                (ObservabilityCompositionResourceNames.SizeSelector, 0, false, "IObservabilityValueSelector<TInput>"),
                (ObservabilityCompositionResourceNames.Clock, 1, false, nameof(TimeProvider))
            ]);
    }

    [Fact]
    public void Design_metadata_provider_describes_observability_ports()
    {
        var metadata = MetadataByType();

        AssertPorts(
            metadata[ObservabilityCompositionNodeTypes.Counter],
            nameof(FlowCounterSnapshot));
        AssertPorts(
            metadata[ObservabilityCompositionNodeTypes.Logger],
            nameof(FlowLogEntry));
        AssertPorts(
            metadata[ObservabilityCompositionNodeTypes.Metrics],
            nameof(FlowMetricSnapshot));
    }

    [Fact]
    public void Design_metadata_provider_describes_counter_options()
    {
        var metadata = MetadataByType()[ObservabilityCompositionNodeTypes.Counter];
        var defaults = new FlowCounterOptions();

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "inputType",
            "name",
            "engine",
            "predicate",
            "expression",
            "expressionId",
            "expressionName",
            "boundedCapacity"
        ], ignoreOrder: false);

        AssertOption(metadata, "inputType", OptionValueKind.Text, defaults.InputType);
        AssertOption(metadata, "name", OptionValueKind.Text, defaultValue: null);
        AssertOption(metadata, "engine", OptionValueKind.Text, defaultValue: null);
        AssertOption(metadata, "predicate", OptionValueKind.Expression, defaultValue: null);
        AssertOption(metadata, "expression", OptionValueKind.Expression, defaultValue: null);
        AssertOption(metadata, "expressionId", OptionValueKind.Text, defaultValue: null);
        AssertOption(metadata, "expressionName", OptionValueKind.Text, defaultValue: null);
        AssertOption(
            metadata,
            "boundedCapacity",
            OptionValueKind.Number,
            defaults.BoundedCapacity,
            min: 1);
    }

    [Fact]
    public void Design_metadata_provider_describes_logger_options()
    {
        var metadata = MetadataByType()[ObservabilityCompositionNodeTypes.Logger];
        var defaults = new FlowLoggerOptions();

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "inputType",
            "level",
            "category",
            "messageTemplate",
            "attributeSelectors",
            "boundedCapacity"
        ], ignoreOrder: false);

        AssertOption(metadata, "inputType", OptionValueKind.Text, defaults.InputType);

        var level = metadata.Options.Single(option => option.Name.Value == "level");
        level.Kind.ShouldBe(OptionValueKind.Enum);
        level.DefaultValue.ShouldBe(defaults.Level);
        level.Choices.Select(choice => choice.Value.Value).ShouldBe([
            FlowLogLevel.Trace.ToString(),
            FlowLogLevel.Debug.ToString(),
            FlowLogLevel.Information.ToString(),
            FlowLogLevel.Warning.ToString(),
            FlowLogLevel.Error.ToString(),
            FlowLogLevel.Critical.ToString()
        ], ignoreOrder: false);

        AssertOption(metadata, "category", OptionValueKind.Text, defaults.Category);
        AssertOption(metadata, "messageTemplate", OptionValueKind.MultilineText, defaultValue: null);

        var selectors = metadata.Options.Single(option => option.Name.Value == "attributeSelectors");
        selectors.Kind.ShouldBe(OptionValueKind.Json);
        selectors.DefaultValue.ShouldBeOfType<string[]>().ShouldBeEmpty();

        AssertOption(
            metadata,
            "boundedCapacity",
            OptionValueKind.Number,
            defaults.BoundedCapacity,
            min: 1);
    }

    [Fact]
    public void Design_metadata_provider_describes_metrics_options()
    {
        var metadata = MetadataByType()[ObservabilityCompositionNodeTypes.Metrics];
        var defaults = new FlowMetricsOptions();

        metadata.Options.Select(option => option.Name.Value).ShouldBe([
            "inputType",
            "name",
            "sizeSelector",
            "boundedCapacity"
        ], ignoreOrder: false);

        AssertOption(metadata, "inputType", OptionValueKind.Text, defaults.InputType);
        AssertOption(metadata, "name", OptionValueKind.Text, defaultValue: null);
        AssertOption(metadata, "sizeSelector", OptionValueKind.Text, defaultValue: null);
        AssertOption(
            metadata,
            "boundedCapacity",
            OptionValueKind.Number,
            defaults.BoundedCapacity,
            min: 1);
    }

    [Fact]
    public void Design_metadata_provider_loads_into_catalog()
    {
        var provider = new ObservabilityComponentDesignMetadataProvider();
        var catalog = ComponentDesignMetadataCatalog.FromProviders([provider]);

        catalog.All.Count.ShouldBe(3);
        catalog.TryGet(
            new ComponentType(ObservabilityCompositionNodeTypes.Counter),
            out var counter).ShouldBeTrue();
        catalog.TryGet(
            new ComponentType(ObservabilityCompositionNodeTypes.Logger),
            out var logger).ShouldBeTrue();
        catalog.TryGet(
            new ComponentType(ObservabilityCompositionNodeTypes.Metrics),
            out var metrics).ShouldBeTrue();

        counter.ShouldNotBeNull().DisplayName.ShouldBe("Counter");
        logger.ShouldNotBeNull().DisplayName.ShouldBe("Logger");
        metrics.ShouldNotBeNull().DisplayName.ShouldBe("Metrics");
    }

    [Fact]
    public async Task Hosted_counter_without_predicate_does_not_require_engine_and_uses_clock()
    {
        var timestamp = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        var clock = new FakeTimeProvider(timestamp);

        await WithNodeAsync<TestMessage, FlowCounterSnapshot>(
            ObservabilityCompositionNodeTypes.Counter,
            registry => registry.RegisterCounter<TestMessage>(),
            async (input, output, _) =>
            {
                var snapshots = Link(output.Source);
                var message = FlowMessage.Create(
                    new TestMessage("alpha", [1, 2], true),
                    new CorrelationId("counter"));

                (await input.Target.SendAsync(message).WaitAsync(Timeout))
                    .ShouldBeTrue();

                var snapshot = await snapshots.ReceiveAsync().WaitAsync(Timeout);
                snapshot.CorrelationId.ShouldBe(message.CorrelationId);
                snapshot.Payload.Count.ShouldBe(1);
                snapshot.Payload.RejectedCount.ShouldBe(0);
                snapshot.Payload.Timestamp.ShouldBe(timestamp);
                snapshot.Payload.LastObservedAt.ShouldBe(timestamp);
            },
            node => node
                .Configure("inputType", "message")
                .Configure("name", "accepted")
                .Resource(ObservabilityCompositionResourceNames.Clock, "fixed"),
            services => services.AddKeyedSingleton<TimeProvider>("fixed", clock));
    }

    [Fact]
    public async Task Hosted_counter_resolves_engine_and_context_factory_for_predicate()
    {
        await WithNodeAsync<TestMessage, FlowCounterSnapshot>(
            ObservabilityCompositionNodeTypes.Counter,
            registry => registry.RegisterCounter<TestMessage>(),
            async (input, output, _) =>
            {
                var snapshots = Link(output.Source);
                var rejected = FlowMessage.Create(
                    new TestMessage("first", [1], false),
                    new CorrelationId("rejected"));
                var accepted = FlowMessage.Create(
                    new TestMessage("second", [1], true),
                    new CorrelationId("accepted"));

                (await input.Target.SendAsync(rejected).WaitAsync(Timeout))
                    .ShouldBeTrue();
                (await input.Target.SendAsync(accepted).WaitAsync(Timeout))
                    .ShouldBeTrue();

                var snapshot = await snapshots.ReceiveAsync().WaitAsync(Timeout);
                snapshot.CorrelationId.ShouldBe(accepted.CorrelationId);
                snapshot.Payload.Count.ShouldBe(1);
                snapshot.Payload.RejectedCount.ShouldBe(1);
            },
            node => node
                .Configure("inputType", "message")
                .Configure("name", "accepted")
                .Configure("predicate", "accepted")
                .Resource(ObservabilityCompositionResourceNames.Engine, "primary")
                .Resource(ObservabilityCompositionResourceNames.ContextFactory, "custom"),
            services =>
            {
                services.AddKeyedSingleton<IFlowExpressionEngine>(
                    "primary",
                    new TestExpressionEngine(
                        (_, context, _) => context.Variables["accepted"]));
                services.AddKeyedSingleton<IFlowMapContextFactory<TestMessage>>(
                    "custom",
                    new TestContextFactory<TestMessage>(
                        message => new FlowMapContext
                        {
                            Variables = new Dictionary<string, object?>(
                                StringComparer.Ordinal)
                            {
                                ["input"] = message,
                                ["accepted"] = message.Enabled
                            }
                        }));
            });
    }

    [Fact]
    public async Task Counter_missing_engine_resource_surfaces_factory_diagnostic()
    {
        await AssertFactoryDiagnosticAsync(
            ObservabilityCompositionNodeTypes.Counter,
            registry => registry.RegisterCounter<TestMessage>(),
            node => node.Configure("predicate", "accepted"),
            "engine");
    }

    [Fact]
    public async Task Hosted_logger_binds_options_and_resolves_attribute_selectors()
    {
        await WithNodeAsync<TestMessage, FlowLogEntry>(
            ObservabilityCompositionNodeTypes.Logger,
            registry => registry.RegisterLogger<TestMessage>(),
            async (input, output, _) =>
            {
                var entries = Link(output.Source);
                var message = FlowMessage.Create(
                    new TestMessage("alpha", [1, 2, 3], true),
                    new CorrelationId("logger"));

                (await input.Target.SendAsync(message).WaitAsync(Timeout))
                    .ShouldBeTrue();

                var entry = await entries.ReceiveAsync().WaitAsync(Timeout);
                entry.CorrelationId.ShouldBe(message.CorrelationId);
                entry.Payload.Level.ShouldBe(FlowLogLevel.Warning);
                entry.Payload.Category.ShouldBe("workflow.test");
                entry.Payload.Message.ShouldBe("Observed alpha:3 #1");
                entry.Payload.Attributes["kind"].ShouldBe("alpha");
                entry.Payload.Attributes["size"].ShouldBe(3);
            },
            node => node
                .Configure("inputType", "message")
                .Configure("level", "Warning")
                .Configure("category", "workflow.test")
                .Configure("messageTemplate", "Observed {kind}:{size} #{sequence}")
                .Configure("attributeSelectors", new[] { "kind", "size" })
                .Resource(
                    ObservabilityCompositionResourceNames.AttributeSelector("kind"),
                    "kind")
                .Resource(
                    ObservabilityCompositionResourceNames.AttributeSelector("size"),
                    "size"),
            services =>
            {
                services.AddKeyedSingleton<IObservabilityValueSelector<TestMessage>>(
                    "kind",
                    new DelegateSelector<TestMessage>((message, _) => message.Kind));
                services.AddKeyedSingleton<IObservabilityValueSelector<TestMessage>>(
                    "size",
                    new DelegateSelector<TestMessage>((message, _) => message.Payload.Length));
            });
    }

    [Fact]
    public async Task Logger_missing_attribute_selector_surfaces_factory_diagnostic()
    {
        await AssertFactoryDiagnosticAsync(
            ObservabilityCompositionNodeTypes.Logger,
            registry => registry.RegisterLogger<TestMessage>(),
            node => node.Configure("attributeSelectors", new[] { "kind" }),
            "attribute:kind");
    }

    [Fact]
    public async Task Hosted_metrics_resolves_size_selector_and_uses_clock_for_rates()
    {
        var firstObservedAt = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        var secondObservedAt = firstObservedAt.AddSeconds(2);
        var clock = new FakeTimeProvider(firstObservedAt);

        await WithNodeAsync<TestMessage, FlowMetricSnapshot>(
            ObservabilityCompositionNodeTypes.Metrics,
            registry => registry.RegisterMetrics<TestMessage>(),
            async (input, output, _) =>
            {
                var snapshots = Link(output.Source);
                var first = FlowMessage.Create(
                    new TestMessage("first", [1, 2], true),
                    new CorrelationId("first"));
                var second = FlowMessage.Create(
                    new TestMessage("second", [1, 2, 3, 4], true),
                    new CorrelationId("second"));

                (await input.Target.SendAsync(first).WaitAsync(Timeout))
                    .ShouldBeTrue();
                (await snapshots.ReceiveAsync().WaitAsync(Timeout))
                    .Payload.Timestamp.ShouldBe(firstObservedAt);

                clock.SetUtcNow(secondObservedAt);
                (await input.Target.SendAsync(second).WaitAsync(Timeout))
                    .ShouldBeTrue();

                var snapshot = await snapshots.ReceiveAsync().WaitAsync(Timeout);
                snapshot.CorrelationId.ShouldBe(second.CorrelationId);
                snapshot.Payload.Count.ShouldBe(2);
                snapshot.Payload.Timestamp.ShouldBe(secondObservedAt);
                snapshot.Payload.LastSize.ShouldBe(4);
                snapshot.Payload.TotalSize.ShouldBe(6);
                snapshot.Payload.CurrentRatePerSecond.ShouldBe(0.5d);
                snapshot.Payload.AverageRatePerSecond.ShouldBe(1d);
            },
            node => node
                .Configure("inputType", "message")
                .Configure("name", "messages")
                .Configure("sizeSelector", "payloadBytes")
                .Resource(ObservabilityCompositionResourceNames.SizeSelector, "payload")
                .Resource(ObservabilityCompositionResourceNames.Clock, "fixed"),
            services =>
            {
                services.AddKeyedSingleton<IObservabilityValueSelector<TestMessage>>(
                    "payload",
                    new DelegateSelector<TestMessage>((message, _) => message.Payload.Length));
                services.AddKeyedSingleton<TimeProvider>("fixed", clock);
            });
    }

    [Fact]
    public async Task Counter_predicate_failure_emits_error_and_continues()
    {
        var calls = 0;
        await WithNodeAsync<int, FlowCounterSnapshot>(
            ObservabilityCompositionNodeTypes.Counter,
            registry => registry.RegisterCounter<int>(),
            async (input, output, descriptor) =>
            {
                var snapshots = Link(output.Source);
                var errors = Link(descriptor.Errors.ShouldNotBeNull());
                var bad = FlowMessage.Create(1, new CorrelationId("bad"));
                var good = FlowMessage.Create(2, new CorrelationId("good"));

                (await input.Target.SendAsync(bad).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(good).WaitAsync(Timeout)).ShouldBeTrue();

                var error = await errors.ReceiveAsync().WaitAsync(Timeout);
                var snapshot = await snapshots.ReceiveAsync().WaitAsync(Timeout);

                error.Code.ShouldBe(ObservabilityErrorCodes.CounterPredicateFailed);
                error.CorrelationId.ShouldBe(bad.CorrelationId);
                snapshot.CorrelationId.ShouldBe(good.CorrelationId);
                snapshot.Payload.Count.ShouldBe(1);
            },
            node => node
                .Configure("predicate", "ok")
                .Resource(ObservabilityCompositionResourceNames.Engine, "primary"),
            services => services.AddKeyedSingleton<IFlowExpressionEngine>(
                "primary",
                new TestExpressionEngine((_, _, _) =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        throw new InvalidOperationException("predicate failed");
                    }

                    return true;
                })));
    }

    [Fact]
    public async Task Logger_selector_failure_emits_error_and_continues()
    {
        await WithNodeAsync<TestMessage, FlowLogEntry>(
            ObservabilityCompositionNodeTypes.Logger,
            registry => registry.RegisterLogger<TestMessage>(),
            async (input, output, descriptor) =>
            {
                var entries = Link(output.Source);
                var errors = Link(descriptor.Errors.ShouldNotBeNull());
                var message = FlowMessage.Create(
                    new TestMessage("alpha", [1], true),
                    new CorrelationId("logger"));

                (await input.Target.SendAsync(message).WaitAsync(Timeout))
                    .ShouldBeTrue();

                var error = await errors.ReceiveAsync().WaitAsync(Timeout);
                var entry = await entries.ReceiveAsync().WaitAsync(Timeout);

                error.Code.ShouldBe(ObservabilityErrorCodes.LoggerAttributeSelectorFailed);
                error.CorrelationId.ShouldBe(message.CorrelationId);
                entry.CorrelationId.ShouldBe(message.CorrelationId);
                entry.Payload.Attributes["kind"].ShouldBe("alpha");
                entry.Payload.Attributes.ContainsKey("broken").ShouldBeFalse();
            },
            node => node
                .Configure("attributeSelectors", new[] { "kind", "broken" })
                .Resource(
                    ObservabilityCompositionResourceNames.AttributeSelector("kind"),
                    "kind")
                .Resource(
                    ObservabilityCompositionResourceNames.AttributeSelector("broken"),
                    "broken"),
            services =>
            {
                services.AddKeyedSingleton<IObservabilityValueSelector<TestMessage>>(
                    "kind",
                    new DelegateSelector<TestMessage>((message, _) => message.Kind));
                services.AddKeyedSingleton<IObservabilityValueSelector<TestMessage>>(
                    "broken",
                    new DelegateSelector<TestMessage>(
                        (_, _) => throw new InvalidOperationException("select failed")));
            });
    }

    [Fact]
    public async Task Metrics_selector_failure_emits_error_and_continues()
    {
        var calls = 0;
        await WithNodeAsync<TestMessage, FlowMetricSnapshot>(
            ObservabilityCompositionNodeTypes.Metrics,
            registry => registry.RegisterMetrics<TestMessage>(),
            async (input, output, descriptor) =>
            {
                var snapshots = Link(output.Source);
                var errors = Link(descriptor.Errors.ShouldNotBeNull());
                var bad = FlowMessage.Create(
                    new TestMessage("first", [1, 2], true),
                    new CorrelationId("bad"));
                var good = FlowMessage.Create(
                    new TestMessage("second", [1, 2, 3], true),
                    new CorrelationId("good"));

                (await input.Target.SendAsync(bad).WaitAsync(Timeout)).ShouldBeTrue();
                (await input.Target.SendAsync(good).WaitAsync(Timeout)).ShouldBeTrue();

                var error = await errors.ReceiveAsync().WaitAsync(Timeout);
                var first = await snapshots.ReceiveAsync().WaitAsync(Timeout);
                var second = await snapshots.ReceiveAsync().WaitAsync(Timeout);

                error.Code.ShouldBe(ObservabilityErrorCodes.MetricsSizeSelectorFailed);
                error.CorrelationId.ShouldBe(bad.CorrelationId);
                first.CorrelationId.ShouldBe(bad.CorrelationId);
                first.Payload.LastSize.ShouldBeNull();
                second.CorrelationId.ShouldBe(good.CorrelationId);
                second.Payload.LastSize.ShouldBe(3);
            },
            node => node.Resource(ObservabilityCompositionResourceNames.SizeSelector, "payload"),
            services => services.AddKeyedSingleton<IObservabilityValueSelector<TestMessage>>(
                "payload",
                new DelegateSelector<TestMessage>((message, _) =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        throw new InvalidOperationException("size failed");
                    }

                    return message.Payload.Length;
                })));
    }

    [Fact]
    public async Task Invalid_logger_configuration_surfaces_factory_diagnostic()
    {
        await AssertFactoryDiagnosticAsync(
            ObservabilityCompositionNodeTypes.Logger,
            registry => registry.RegisterLogger<TestMessage>(),
            node => node.Configure("level", "Nope"),
            "level");
    }

    [Fact]
    public async Task Invalid_counter_options_surface_factory_diagnostic()
    {
        await AssertFactoryDiagnosticAsync(
            ObservabilityCompositionNodeTypes.Counter,
            registry => registry.RegisterCounter<TestMessage>(),
            node => node.Configure("boundedCapacity", 0),
            "boundedCapacity");
    }

    [Fact]
    public async Task Invalid_logger_options_surface_factory_diagnostic()
    {
        await AssertFactoryDiagnosticAsync(
            ObservabilityCompositionNodeTypes.Logger,
            registry => registry.RegisterLogger<TestMessage>(),
            node => node.Configure("inputType", " "),
            "inputType");
    }

    [Fact]
    public async Task Invalid_metrics_options_surface_factory_diagnostic()
    {
        await AssertFactoryDiagnosticAsync(
            ObservabilityCompositionNodeTypes.Metrics,
            registry => registry.RegisterMetrics<TestMessage>(),
            node => node.Configure("boundedCapacity", 0),
            "boundedCapacity");
    }

    private static void AssertMetadata<TOutput>(
        CompositionNodeRegistry registry,
        string nodeType)
    {
        var registration = registry.Registrations[nodeType];
        registration.Inputs[ObservabilityCompositionPortNames.Input].MessageType
            .ShouldBe(typeof(TestMessage));
        registration.Outputs[ObservabilityCompositionPortNames.Output].MessageType
            .ShouldBe(typeof(TOutput));
    }

    private static Dictionary<string, ComponentDesignMetadata> MetadataByType()
        => new ObservabilityComponentDesignMetadataProvider()
            .GetMetadata()
            .ToDictionary(metadata => metadata.Type.Value, StringComparer.Ordinal);

    private static void AssertPorts(
        ComponentDesignMetadata metadata,
        string outputType)
    {
        metadata.Ports.Count.ShouldBe(2);

        var input = metadata.Ports[0];
        input.Name.Value.ShouldBe(ObservabilityCompositionPortNames.Input);
        input.Direction.ShouldBe(PortDirection.Input);
        input.Order.ShouldBe(0);
        input.ValueType?.Value.ShouldBe("TInput");
        input.IsPrimary.ShouldBeTrue();

        var output = metadata.Ports[1];
        output.Name.Value.ShouldBe(ObservabilityCompositionPortNames.Output);
        output.Direction.ShouldBe(PortDirection.Output);
        output.Order.ShouldBe(1);
        output.ValueType?.Value.ShouldBe(outputType);
        output.IsPrimary.ShouldBeTrue();
    }

    private static void AssertOption(
        ComponentDesignMetadata metadata,
        string name,
        OptionValueKind kind,
        object? defaultValue,
        double? min = null)
    {
        var option = metadata.Options.Single(option => option.Name.Value == name);
        option.Kind.ShouldBe(kind);
        option.DefaultValue.ShouldBe(defaultValue);
        option.Min.ShouldBe(min);
    }

    private static void AssertResources(
        ComponentDesignMetadata metadata,
        IReadOnlyList<(string Name, int Order, bool IsRequired, string ValueType)> expected)
    {
        metadata.Resources.Select(resource => (
            resource.Name.Value,
            resource.Order,
            resource.IsRequired,
            resource.ValueType?.Value!)).ShouldBe(expected);
    }

    private static async Task AssertFactoryDiagnosticAsync(
        string nodeType,
        Func<CompositionNodeRegistry, CompositionNodeRegistry> register,
        Action<NodeDefinitionBuilder> configureNode,
        string expectedMessage)
    {
        var services = new ServiceCollection();
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "node",
                    nodeType,
                    configureNode))
                .Build())
            .RegisterNodes(registry => register(registry))
            .Configure(options => options.ThrowOnBuildFailure = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var host = provider.GetRequiredService<ICompositionRuntimeHost>();
        host.Runtime.ShouldBeNull();
        host.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == CompositionDiagnosticCode.FactoryFailed &&
            diagnostic.Message.Contains(
                expectedMessage,
                StringComparison.OrdinalIgnoreCase));
    }

    private static async Task WithNodeAsync<TInput, TOutput>(
        string nodeType,
        Func<CompositionNodeRegistry, CompositionNodeRegistry> register,
        Func<CompositionInputPort<TInput>, CompositionOutputPort<TOutput>, ComposedNode, Task> run,
        Action<NodeDefinitionBuilder>? configureNode = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        configureServices?.Invoke(services);
        services
            .AddFluxFlowComposition(CompositionDefinitionBuilder
                .Create()
                .Workflow("main", workflow => workflow.Node(
                    "node",
                    nodeType,
                    configureNode))
                .Build())
            .RegisterNodes(registry => register(registry))
            .Configure(options => options.StartRuntimeWithHost = false);

        await using var provider = services.BuildServiceProvider();
        await BuildCompositionAsync(provider);

        var descriptor = provider.GetRequiredService<ICompositionRuntimeHost>()
            .Runtime.ShouldNotBeNull()
            .Nodes.ShouldHaveSingleItem()
            .Descriptor;
        var input = descriptor.Inputs[ObservabilityCompositionPortNames.Input]
            .ShouldBeOfType<CompositionInputPort<TInput>>();
        var output = descriptor.Outputs[ObservabilityCompositionPortNames.Output]
            .ShouldBeOfType<CompositionOutputPort<TOutput>>();

        await run(input, output, descriptor);
    }

    private static async Task BuildCompositionAsync(IServiceProvider provider)
    {
        var hostedService = provider.GetServices<IHostedService>().ShouldHaveSingleItem();
        await hostedService.StartAsync(CancellationToken.None);
    }

    private static BufferBlock<T> Link<T>(ISourceBlock<T> source)
    {
        var buffer = new BufferBlock<T>();
        source.LinkTo(buffer, new DataflowLinkOptions { PropagateCompletion = true });
        return buffer;
    }

    private sealed record TestMessage(string Kind, byte[] Payload, bool Enabled);

    private sealed class TestExpressionEngine(
        Func<string, FlowMapContext, Type, object?> evaluate)
        : IFlowExpressionEngine
    {
        public string Name => "test";

        public object? Evaluate(
            string expression,
            FlowMapContext context,
            Type resultType)
            => evaluate(expression, context, resultType);
    }

    private sealed class TestContextFactory<TInput>(
        Func<TInput, FlowMapContext> create)
        : IFlowMapContextFactory<TInput>
    {
        public FlowMapContext Create(TInput input)
            => create(input);
    }

    private sealed class DelegateSelector<TInput>(
        Func<TInput, ObservabilityNodeContext, object?> selector)
        : IObservabilityValueSelector<TInput>
    {
        public object? Select(TInput input, ObservabilityNodeContext context)
            => selector(input, context);
    }
}
