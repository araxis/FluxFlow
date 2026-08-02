using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Composition.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Composition.Model;
using FluxFlow.Engine.Ports;
using FluxFlow.Engine.Signals;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationOutputCaptureTests
{
    private static readonly ApplicationAddress Output =
        ApplicationAddress.WorkflowPort("Main", "Source", "Output");
    private static readonly ApplicationAddress LinkedInput =
        ApplicationAddress.WorkflowPort("Main", "Linked", "Input");
    private static readonly ApplicationAddress RevisionInput =
        ApplicationAddress.WorkflowPort("Main", "Revision", "Input");

    [Fact]
    public async Task Unconfigured_output_dispatches_without_a_capture_dependency()
    {
        var routing = new ApplicationRevisionRouting();
        var output = CreateOutput(routing);
        var source = new BufferBlock<FlowMessage<string>>();
        using var attachment = output.Attach(source);
        using var receive = output.RegisterReceive(traceId: null);
        var observed = output.Observe(capacity: 1);
        await using var observation = observed.Observation.ShouldNotBeNull();
        var message = FlowMessage.Create("ordinary");

        source.Post(message).ShouldBeTrue();

        var received = await receive.Task.WaitAsync(TestTimeout);
        var observedMessage = await observation.Messages.ReceiveAsync().WaitAsync(TestTimeout);
        received.Status.ShouldBe(PortReceiveStatus.Received);
        received.Message.ShouldBeSameAs(message);
        observedMessage.ShouldBeSameAs(message);

        output.Complete();
        await output.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task Capture_completes_before_revision_links_connected_links_receives_and_observations()
    {
        var capture = new GatedCapture<string>();
        var rejections = new ConcurrentQueue<ApplicationPortRejection>();
        var routing = new ApplicationRevisionRouting();
        var revisionRoute = new RecordingRevisionRoute(Output, RevisionInput);
        routing.Swap(new ApplicationRevisionRouting.Snapshot(
            new Dictionary<ApplicationAddress, IReadOnlyList<IApplicationRevisionRoute>>
            {
                [Output] = [revisionRoute]
            },
            new HashSet<ApplicationRevisionRouting.RouteIdentity>()));
        var output = CreateOutput(routing, capture, rejections.Enqueue);
        var input = new ApplicationInputPort<string>(
            LinkedInput,
            capacity: 1,
            rejections.Enqueue,
            static _ => { });
        var linkedMessages = new BufferBlock<FlowMessage<string>>();
        await using var inputAttachment = await input.AttachAsync(
            linkedMessages,
            CancellationToken.None);
        using var connection = output.Connect(input, CompileLink());
        var source = new BufferBlock<FlowMessage<string>>();
        using var outputAttachment = output.Attach(source);
        using var receive = output.RegisterReceive(traceId: null);
        var observed = output.Observe(capacity: 1);
        await using var observation = observed.Observation.ShouldNotBeNull();
        var message = FlowMessage.Create("captured-first");

        source.Post(message).ShouldBeTrue();
        var invocation = await capture.NextAsync();

        invocation.Message.ShouldBeSameAs(message);
        receive.Task.IsCompleted.ShouldBeFalse();
        revisionRoute.Messages.ShouldBeEmpty();
        linkedMessages.TryReceive(out _).ShouldBeFalse();
        ((IReceivableSourceBlock<FlowMessage<string>>)observation.Messages)
            .TryReceive(out _).ShouldBeFalse();
        rejections.ShouldBeEmpty();

        invocation.Release();

        (await receive.Task.WaitAsync(TestTimeout)).Message.ShouldBeSameAs(message);
        (await revisionRoute.NextAsync()).ShouldBeSameAs(message);
        (await linkedMessages.ReceiveAsync().WaitAsync(TestTimeout)).ShouldBeSameAs(message);
        (await observation.Messages.ReceiveAsync().WaitAsync(TestTimeout)).ShouldBeSameAs(message);
        rejections.ShouldBeEmpty();

        output.Complete();
        await output.Completion.WaitAsync(TestTimeout);
        input.Abort();
    }

    [Fact]
    public async Task Capture_failure_reports_the_exact_rejection_and_never_dispatches()
    {
        var expected = new InvalidOperationException("capture failed");
        var rejections = new ConcurrentQueue<ApplicationPortRejection>();
        var activities = new ConcurrentQueue<ApplicationPortActivity>();
        var capture = new ThrowingCapture<string>(expected);
        var routing = new ApplicationRevisionRouting();
        var revisionRoute = new RecordingRevisionRoute(Output, RevisionInput);
        routing.Swap(new ApplicationRevisionRouting.Snapshot(
            new Dictionary<ApplicationAddress, IReadOnlyList<IApplicationRevisionRoute>>
            {
                [Output] = [revisionRoute]
            },
            new HashSet<ApplicationRevisionRouting.RouteIdentity>()));
        var output = new ApplicationOutputPort<string>(
            Output,
            capacity: 1,
            rejections.Enqueue,
            activities.Enqueue,
            routing,
            capture);
        var input = new ApplicationInputPort<string>(
            LinkedInput,
            capacity: 1,
            rejections.Enqueue,
            activities.Enqueue);
        var linkedMessages = new BufferBlock<FlowMessage<string>>();
        await using var inputAttachment = await input.AttachAsync(
            linkedMessages,
            CancellationToken.None);
        using var connection = output.Connect(input, CompileLink());
        var source = new BufferBlock<FlowMessage<string>>();
        using var outputAttachment = output.Attach(source);
        using var receive = output.RegisterReceive(traceId: null);
        var observed = output.Observe(capacity: 1);
        await using var observation = observed.Observation.ShouldNotBeNull();
        var message = FlowMessage.Create("rejected");

        source.Post(message).ShouldBeTrue();

        var failure = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await output.Completion.WaitAsync(TestTimeout));
        failure.ShouldBeSameAs(expected);
        capture.Messages.ShouldBe([message]);
        revisionRoute.Messages.ShouldBeEmpty();
        linkedMessages.TryReceive(out _).ShouldBeFalse();
        ((IReceivableSourceBlock<FlowMessage<string>>)observation.Messages)
            .TryReceive(out _).ShouldBeFalse();
        (await receive.Task.WaitAsync(TestTimeout)).Status.ShouldBe(PortReceiveStatus.Completed);
        var observationFailure = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await observation.Completion.WaitAsync(TestTimeout));
        observationFailure.Message.ShouldBe(expected.Message);
        activities.ShouldBeEmpty();

        var rejection = rejections.ShouldHaveSingleItem();
        rejection.Port.ShouldBe(Output);
        rejection.MessageId.ShouldBe(message.MessageId);
        rejection.CorrelationId.ShouldBe(message.CorrelationId);
        rejection.TraceId.ShouldBe(message.TraceId);
        rejection.Reason.ShouldBe(ApplicationPortRejectionReason.OutputCaptureFailed);
        rejection.Exception.ShouldBeSameAs(expected);

        output.Abort();
        input.Abort();
    }

    [Fact]
    public async Task Capture_serializes_messages_and_propagates_bounded_backpressure()
    {
        var capture = new GatedCapture<string>();
        var output = CreateOutput(new ApplicationRevisionRouting(), capture, capacity: 1);
        var source = new BufferBlock<FlowMessage<string>>(new DataflowBlockOptions
        {
            BoundedCapacity = 1
        });
        using var attachment = output.Attach(source);
        var observed = output.Observe(capacity: 4);
        await using var observation = observed.Observation.ShouldNotBeNull();
        var messages = Enumerable.Range(1, 4)
            .Select(index => FlowMessage.Create($"message-{index}"))
            .ToArray();

        (await source.SendAsync(messages[0]).WaitAsync(TestTimeout)).ShouldBeTrue();
        var first = await capture.NextAsync();
        (await source.SendAsync(messages[1]).WaitAsync(TestTimeout)).ShouldBeTrue();
        (await source.SendAsync(messages[2]).WaitAsync(TestTimeout)).ShouldBeTrue();
        var fourthSend = source.SendAsync(messages[3]);

        fourthSend.IsCompleted.ShouldBeFalse();
        capture.Messages.ShouldBe([messages[0]]);
        ((IReceivableSourceBlock<FlowMessage<string>>)observation.Messages)
            .TryReceive(out _).ShouldBeFalse();

        first.Release();
        var second = await capture.NextAsync();
        second.Message.ShouldBeSameAs(messages[1]);
        (await fourthSend.WaitAsync(TestTimeout)).ShouldBeTrue();
        second.Release();
        var third = await capture.NextAsync();
        third.Message.ShouldBeSameAs(messages[2]);
        third.Release();
        var fourth = await capture.NextAsync();
        fourth.Message.ShouldBeSameAs(messages[3]);
        fourth.Release();

        var observedMessages = new List<FlowMessage<string>>();
        for (var index = 0; index < messages.Length; index++)
        {
            observedMessages.Add(
                await observation.Messages.ReceiveAsync().WaitAsync(TestTimeout));
        }

        capture.Messages.ShouldBe(messages);
        observedMessages.ShouldBe(messages);

        output.Complete();
        await output.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task Drain_waits_for_in_flight_capture_and_dispatches_before_completing()
    {
        var capture = new GatedCapture<string>();
        var output = CreateOutput(new ApplicationRevisionRouting(), capture);
        var source = new BufferBlock<FlowMessage<string>>();
        using var attachment = output.Attach(source);
        using var receive = output.RegisterReceive(traceId: null);
        var message = FlowMessage.Create("drain-me");
        using var timeout = new CancellationTokenSource(TestTimeout);

        source.Post(message).ShouldBeTrue();
        var invocation = await capture.NextAsync();
        var drain = output.DrainAsync(timeout.Token).AsTask();

        drain.IsCompleted.ShouldBeFalse();
        receive.Task.IsCompleted.ShouldBeFalse();

        invocation.Release();

        await drain.WaitAsync(TestTimeout);
        (await receive.Task.WaitAsync(TestTimeout)).Message.ShouldBeSameAs(message);

        output.Complete();
        await output.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task Abort_cancels_in_flight_capture_without_dispatching()
    {
        var capture = new GatedCapture<string>();
        var rejections = new ConcurrentQueue<ApplicationPortRejection>();
        var output = CreateOutput(
            new ApplicationRevisionRouting(),
            capture,
            rejections.Enqueue);
        var source = new BufferBlock<FlowMessage<string>>();
        using var attachment = output.Attach(source);
        using var receive = output.RegisterReceive(traceId: null);
        var observed = output.Observe(capacity: 1);
        await using var observation = observed.Observation.ShouldNotBeNull();
        var message = FlowMessage.Create("abort-me");

        source.Post(message).ShouldBeTrue();
        var invocation = await capture.NextAsync();

        output.Abort();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await invocation.Completion.WaitAsync(TestTimeout));
        await output.Completion.WaitAsync(TestTimeout);
        (await receive.Task.WaitAsync(TestTimeout)).Status.ShouldBe(PortReceiveStatus.Completed);
        ((IReceivableSourceBlock<FlowMessage<string>>)observation.Messages)
            .TryReceive(out _).ShouldBeFalse();
        await observation.Completion.WaitAsync(TestTimeout);
        rejections.ShouldBeEmpty();
    }

    [Fact]
    public async Task Runtime_builder_resolves_capture_by_exact_output_address_and_payload_type()
    {
        var capture = new RecordingCapture<string>();
        var resolver = new RecordingResolver(Output, capture);

        await using var runtime = new ApplicationPortRuntimeBuilder(resolver)
            .AddOutput<string>(Output)
            .Build();

        resolver.Resolutions.ShouldContain(new CaptureResolution(Output, typeof(string)));
        resolver.Resolutions.Count(resolution => resolution.Address == Output).ShouldBe(1);
        resolver.Resolutions.ShouldContain(
            new CaptureResolution(ApplicationAddress.SystemEvents, typeof(ApplicationSystemEvent)));
        resolver.Resolutions.ShouldContain(
            new CaptureResolution(ApplicationAddress.SystemDiagnostics, typeof(ApplicationDiagnostic)));

        var source = new BufferBlock<FlowMessage<string>>();
        using var attachment = runtime.AttachOutput(Output, source);
        var receive = runtime.ReceiveAsync<string>(Output, timeout: TestTimeout);
        var message = FlowMessage.Create("resolved");
        source.Post(message).ShouldBeTrue();

        (await receive).Message.ShouldBeSameAs(message);
        capture.Messages.ShouldBe([message]);
    }

    [Fact]
    public void Capture_contracts_are_typed_and_do_not_expose_service_location_or_provider_dependencies()
    {
        var resolverMethod = typeof(IApplicationOutputCaptureResolver)
            .GetMethod(nameof(IApplicationOutputCaptureResolver.Resolve))
            .ShouldNotBeNull();
        resolverMethod.IsGenericMethodDefinition.ShouldBeTrue();
        resolverMethod.GetParameters().Select(static parameter => parameter.ParameterType)
            .ShouldBe([typeof(ApplicationAddress)]);

        var captureMethod = typeof(IApplicationOutputCapture<string>)
            .GetMethod(nameof(IApplicationOutputCapture<string>.CaptureAsync))
            .ShouldNotBeNull();
        captureMethod.GetParameters().Select(static parameter => parameter.ParameterType)
            .ShouldBe([typeof(FlowMessage<string>), typeof(CancellationToken)]);
        captureMethod.ReturnType.ShouldBe(typeof(ValueTask));

        var referencedAssemblies = typeof(IApplicationOutputCaptureResolver).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .ToArray();
        referencedAssemblies.ShouldNotContain("FluxFlow.Engine.DurableInput");
        referencedAssemblies.ShouldNotContain("FluxFlow.Engine.DurableInput.SqlFile");
    }

    private static TimeSpan TestTimeout => TimeSpan.FromSeconds(5);

    private static ApplicationOutputPort<string> CreateOutput(
        ApplicationRevisionRouting routing,
        IApplicationOutputCapture<string>? capture = null,
        Action<ApplicationPortRejection>? report = null,
        int capacity = 4)
        => new(
            Output,
            capacity,
            report ?? (static _ => { }),
            static _ => { },
            routing,
            capture);

    private static CompiledApplicationLink CompileLink()
    {
        var definition = ApplicationDefinitionJson.Deserialize(
            """
            {
              "Resources": {},
              "Workflows": {
                "Main": {
                  "Source": { "Type": "source", "Output": "Linked.Input" },
                  "Linked": { "Type": "sink" }
                }
              }
            }
            """);
        var registry = new ComponentCatalog(
        [
            new ComponentDescriptor(
                "source",
                UnusedFactory,
                outputs: [ComponentPorts.Metadata<string>("Output")]),
            new ComponentDescriptor(
                "sink",
                UnusedFactory,
                inputs: [ComponentPorts.Metadata<string>("Input")])
        ]);
        var result = new ApplicationLinkCompiler(registry).Compile(definition);
        result.IsValid.ShouldBeTrue(
            string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        return result.Links.ShouldHaveSingleItem();
    }

    private static ValueTask<ComponentInstance> UnusedFactory(ComponentActivationContext _)
        => throw new InvalidOperationException("Link compilation must not activate node factories.");

    private sealed class RecordingRevisionRoute(
        ApplicationAddress source,
        ApplicationAddress target) : IApplicationRevisionRoute
    {
        private readonly Channel<FlowMessage<string>> _delivered = Channel.CreateUnbounded<FlowMessage<string>>();
        private readonly ConcurrentQueue<FlowMessage<string>> _messages = new();

        public ApplicationAddress Source => source;

        public ApplicationAddress Target => target;

        public IReadOnlyList<FlowMessage<string>> Messages => _messages.ToArray();

        public void TryDeliver(object message)
        {
            var typed = (FlowMessage<string>)message;
            _messages.Enqueue(typed);
            _delivered.Writer.TryWrite(typed).ShouldBeTrue();
        }

        public async Task<FlowMessage<string>> NextAsync()
        {
            using var timeout = new CancellationTokenSource(TestTimeout);
            return await _delivered.Reader.ReadAsync(timeout.Token);
        }
    }

    private sealed class GatedCapture<T> : IApplicationOutputCapture<T>
    {
        private readonly Channel<CaptureInvocation<T>> _invocations =
            Channel.CreateUnbounded<CaptureInvocation<T>>();
        private readonly ConcurrentQueue<FlowMessage<T>> _messages = new();

        public IReadOnlyList<FlowMessage<T>> Messages => _messages.ToArray();

        public ValueTask CaptureAsync(
            FlowMessage<T> message,
            CancellationToken cancellationToken = default)
        {
            var invocation = new CaptureInvocation<T>(message, cancellationToken);
            _messages.Enqueue(message);
            _invocations.Writer.TryWrite(invocation).ShouldBeTrue();
            return new ValueTask(invocation.Completion);
        }

        public async Task<CaptureInvocation<T>> NextAsync()
        {
            using var timeout = new CancellationTokenSource(TestTimeout);
            return await _invocations.Reader.ReadAsync(timeout.Token);
        }
    }

    private sealed class CaptureInvocation<T>
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CaptureInvocation(FlowMessage<T> message, CancellationToken cancellationToken)
        {
            Message = message;
            Completion = _release.Task.WaitAsync(cancellationToken);
        }

        public FlowMessage<T> Message { get; }

        public Task Completion { get; }

        public void Release() => _release.TrySetResult().ShouldBeTrue();
    }

    private sealed class ThrowingCapture<T>(Exception exception) : IApplicationOutputCapture<T>
    {
        private readonly ConcurrentQueue<FlowMessage<T>> _messages = new();

        public IReadOnlyList<FlowMessage<T>> Messages => _messages.ToArray();

        public ValueTask CaptureAsync(
            FlowMessage<T> message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _messages.Enqueue(message);
            return ValueTask.FromException(exception);
        }
    }

    private sealed class RecordingCapture<T> : IApplicationOutputCapture<T>
    {
        private readonly ConcurrentQueue<FlowMessage<T>> _messages = new();

        public IReadOnlyList<FlowMessage<T>> Messages => _messages.ToArray();

        public ValueTask CaptureAsync(
            FlowMessage<T> message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _messages.Enqueue(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingResolver(
        ApplicationAddress configuredAddress,
        IApplicationOutputCapture<string> capture) : IApplicationOutputCaptureResolver
    {
        private readonly List<CaptureResolution> _resolutions = [];

        public IReadOnlyList<CaptureResolution> Resolutions => _resolutions;

        public IApplicationOutputCapture<T>? Resolve<T>(ApplicationAddress address)
        {
            _resolutions.Add(new CaptureResolution(address, typeof(T)));
            return address == configuredAddress && typeof(T) == typeof(string)
                ? (IApplicationOutputCapture<T>)capture
                : null;
        }
    }

    private sealed record CaptureResolution(ApplicationAddress Address, Type PayloadType);
}
