using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition;
using FluxFlow.Core.Addressing;
using FluxFlow.Composition.Links;
using FluxFlow.Json.Model;
using FluxFlow.Engine.Ports;
using FluxFlow.Engine.Signals;
using FluxFlow.Nodes;
using FluxFlow.Data;
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
    public async Task Terminal_output_dispatches_without_a_capture_dependency()
    {
        var routing = new ApplicationRevisionRouting();
        var output = CreateOutput(routing, discardUnmatched: true);
        var source = new BufferBlock<FlowMessage>();
        using var attachment = output.Attach(source);
        using var receive = output.RegisterReceive();
        var observed = output.Observe<FlowValue>(capacity: 1);
        await using var observation = observed.Observation.ShouldNotBeNull();
        var message = FlowMessage.Create(FlowValue.From("ordinary"));

        source.Post(message).ShouldBeTrue();

        var received = await receive.Task.WaitAsync(TestTimeout);
        var observedMessage = await observation.Messages.ReceiveAsync().WaitAsync(TestTimeout);
        received.Status.ShouldBe(PortReceiveStatus.Received);
        received.Message.ShouldBeSameAs(message);
        AssertProjection(observedMessage, message);

        output.Complete();
        await output.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task Capture_completes_before_revision_links_connected_links_receives_and_observations()
    {
        var capture = new GatedCapture();
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
        var input = new ApplicationInputPort(
            LinkedInput,
            capacity: 1,
            rejections.Enqueue,
            static _ => { });
        var linkedMessages = new BufferBlock<FlowMessage>();
        await using var inputAttachment = await input.AttachAsync(
            linkedMessages,
            CancellationToken.None);
        using var connection = output.Connect(input, CompileLink());
        var source = new BufferBlock<FlowMessage>();
        using var outputAttachment = output.Attach(source);
        using var receive = output.RegisterReceive();
        var observed = output.Observe<FlowValue>(capacity: 1);
        await using var observation = observed.Observation.ShouldNotBeNull();
        var message = FlowMessage.Create(FlowValue.From("captured-first"));

        source.Post(message).ShouldBeTrue();
        var invocation = await capture.NextAsync();

        AssertProjection(invocation.Message, message);
        receive.Task.IsCompleted.ShouldBeFalse();
        revisionRoute.Messages.ShouldBeEmpty();
        linkedMessages.TryReceive(out _).ShouldBeFalse();
        ((IReceivableSourceBlock<FlowMessage<FlowValue>>)observation.Messages)
            .TryReceive(out _).ShouldBeFalse();
        rejections.ShouldBeEmpty();

        invocation.Release();

        (await receive.Task.WaitAsync(TestTimeout)).Message.ShouldBeSameAs(message);
        (await revisionRoute.NextAsync()).ShouldBeSameAs(message);
        (await linkedMessages.ReceiveAsync().WaitAsync(TestTimeout)).ShouldBeSameAs(message);
        AssertProjection(await observation.Messages.ReceiveAsync().WaitAsync(TestTimeout), message);
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
        var capture = new ThrowingCapture(expected);
        var routing = new ApplicationRevisionRouting();
        var revisionRoute = new RecordingRevisionRoute(Output, RevisionInput);
        routing.Swap(new ApplicationRevisionRouting.Snapshot(
            new Dictionary<ApplicationAddress, IReadOnlyList<IApplicationRevisionRoute>>
            {
                [Output] = [revisionRoute]
            },
            new HashSet<ApplicationRevisionRouting.RouteIdentity>()));
        var output = new ApplicationOutputPort(
            Output,
            capacity: 1,
            rejections.Enqueue,
            activities.Enqueue,
            routing,
            capture);
        var input = new ApplicationInputPort(
            LinkedInput,
            capacity: 1,
            rejections.Enqueue,
            activities.Enqueue);
        var linkedMessages = new BufferBlock<FlowMessage>();
        await using var inputAttachment = await input.AttachAsync(
            linkedMessages,
            CancellationToken.None);
        using var connection = output.Connect(input, CompileLink());
        var source = new BufferBlock<FlowMessage>();
        using var outputAttachment = output.Attach(source);
        using var receive = output.RegisterReceive();
        var observed = output.Observe<FlowValue>(capacity: 1);
        await using var observation = observed.Observation.ShouldNotBeNull();
        var message = FlowMessage.Create(FlowValue.From("rejected"));

        source.Post(message).ShouldBeTrue();

        var failure = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await output.Completion.WaitAsync(TestTimeout));
        failure.ShouldBeSameAs(expected);
        AssertProjection(capture.Messages.ShouldHaveSingleItem(), message);
        revisionRoute.Messages.ShouldBeEmpty();
        linkedMessages.TryReceive(out _).ShouldBeFalse();
        ((IReceivableSourceBlock<FlowMessage<FlowValue>>)observation.Messages)
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
        var capture = new GatedCapture();
        // Total capacity includes the active capture plus one queued message.
        var output = CreateOutput(new ApplicationRevisionRouting(), capture, capacity: 2, discardUnmatched: true);
        var source = new BufferBlock<FlowMessage>(new DataflowBlockOptions
        {
            BoundedCapacity = 1
        });
        using var attachment = output.Attach(source);
        var observed = output.Observe<FlowValue>(capacity: 4);
        await using var observation = observed.Observation.ShouldNotBeNull();
        var messages = Enumerable.Range(1, 4)
            .Select(index => FlowMessage.Create(FlowValue.From($"message-{index}")))
            .ToArray();

        (await source.SendAsync(messages[0]).WaitAsync(TestTimeout)).ShouldBeTrue();
        var first = await capture.NextAsync();
        (await source.SendAsync(messages[1]).WaitAsync(TestTimeout)).ShouldBeTrue();
        (await source.SendAsync(messages[2]).WaitAsync(TestTimeout)).ShouldBeTrue();
        var fourthSend = source.SendAsync(messages[3]);

        fourthSend.IsCompleted.ShouldBeFalse();
        AssertProjection(capture.Messages.ShouldHaveSingleItem(), messages[0]);
        ((IReceivableSourceBlock<FlowMessage<FlowValue>>)observation.Messages)
            .TryReceive(out _).ShouldBeFalse();

        first.Release();
        var second = await capture.NextAsync();
        AssertProjection(second.Message, messages[1]);
        (await fourthSend.WaitAsync(TestTimeout)).ShouldBeTrue();
        second.Release();
        var third = await capture.NextAsync();
        AssertProjection(third.Message, messages[2]);
        third.Release();
        var fourth = await capture.NextAsync();
        AssertProjection(fourth.Message, messages[3]);
        fourth.Release();

        var observedMessages = new List<FlowMessage<FlowValue>>();
        for (var index = 0; index < messages.Length; index++)
        {
            observedMessages.Add(
                await observation.Messages.ReceiveAsync().WaitAsync(TestTimeout));
        }

        capture.Messages.Count.ShouldBe(messages.Length);
        observedMessages.Count.ShouldBe(messages.Length);
        for (var index = 0; index < messages.Length; index++)
        {
            AssertProjection(capture.Messages[index], messages[index]);
            AssertProjection(observedMessages[index], messages[index]);
        }

        output.Complete();
        await output.Completion.WaitAsync(TestTimeout);
    }

    [Fact]
    public async Task Drain_waits_for_in_flight_capture_and_dispatches_before_completing()
    {
        var capture = new GatedCapture();
        var output = CreateOutput(new ApplicationRevisionRouting(), capture, discardUnmatched: true);
        var source = new BufferBlock<FlowMessage>();
        using var attachment = output.Attach(source);
        using var receive = output.RegisterReceive();
        var message = FlowMessage.Create(FlowValue.From("drain-me"));
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
        var capture = new GatedCapture();
        var rejections = new ConcurrentQueue<ApplicationPortRejection>();
        var output = CreateOutput(
            new ApplicationRevisionRouting(),
            capture,
            rejections.Enqueue);
        var source = new BufferBlock<FlowMessage>();
        using var attachment = output.Attach(source);
        using var receive = output.RegisterReceive();
        var observed = output.Observe<FlowValue>(capacity: 1);
        await using var observation = observed.Observation.ShouldNotBeNull();
        var message = FlowMessage.Create(FlowValue.From("abort-me"));

        source.Post(message).ShouldBeTrue();
        var invocation = await capture.NextAsync();

        output.Abort();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await invocation.Completion.WaitAsync(TestTimeout));
        await output.Completion.WaitAsync(TestTimeout);
        (await receive.Task.WaitAsync(TestTimeout)).Status.ShouldBe(PortReceiveStatus.Completed);
        ((IReceivableSourceBlock<FlowMessage<FlowValue>>)observation.Messages)
            .TryReceive(out _).ShouldBeFalse();
        await observation.Completion.WaitAsync(TestTimeout);
        rejections.ShouldBeEmpty();
    }

    [Fact]
    public async Task Runtime_builder_resolves_capture_by_exact_output_address()
    {
        var capture = new RecordingCapture();
        var resolver = new RecordingResolver(Output, capture);

        await using var runtime = new ApplicationPortRuntimeBuilder(resolver)
            .AddOutput(Output, discardUnmatched: true)
            .Build();

        resolver.Resolutions.ShouldContain(Output);
        resolver.Resolutions.Count(resolution => resolution == Output).ShouldBe(1);
        resolver.Resolutions.ShouldContain(
            ApplicationAddress.SystemEvents);
        resolver.Resolutions.ShouldContain(
            ApplicationAddress.SystemDiagnostics);

        var source = new BufferBlock<FlowMessage>();
        using var attachment = runtime.AttachOutput(Output, source);
        var receive = runtime.ReceiveAsync<FlowValue>(Output, timeout: TestTimeout);
        var message = FlowMessage.Create(FlowValue.From("resolved"));
        source.Post(message).ShouldBeTrue();

        AssertProjection((await receive).Message, message);
        AssertProjection(capture.Messages.ShouldHaveSingleItem(), message);
    }

    [Fact]
    public void Capture_contracts_are_canonical_and_do_not_expose_service_location_or_provider_dependencies()
    {
        var resolverMethod = typeof(IApplicationOutputCaptureResolver)
            .GetMethod(nameof(IApplicationOutputCaptureResolver.Resolve))
            .ShouldNotBeNull();
        resolverMethod.IsGenericMethodDefinition.ShouldBeFalse();
        resolverMethod.GetParameters().Select(static parameter => parameter.ParameterType)
            .ShouldBe([typeof(ApplicationAddress)]);

        var captureMethod = typeof(IApplicationOutputCapture)
            .GetMethod(nameof(IApplicationOutputCapture.CaptureAsync))
            .ShouldNotBeNull();
        captureMethod.GetParameters().Select(static parameter => parameter.ParameterType)
            .ShouldBe([typeof(FlowMessage), typeof(CancellationToken)]);
        captureMethod.ReturnType.ShouldBe(typeof(ValueTask));

        var referencedAssemblies = typeof(IApplicationOutputCaptureResolver).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .ToArray();
        referencedAssemblies.ShouldNotContain("FluxFlow.Engine.DurableInput");
        referencedAssemblies.ShouldNotContain("FluxFlow.Engine.DurableInput.SqlFile");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Capture_receives_original_canonical_value_or_error_envelope(bool isError)
    {
        var capture = new RecordingCapture();
        var output = CreateOutput(new ApplicationRevisionRouting(), capture, discardUnmatched: true);
        var source = new BufferBlock<FlowMessage>();
        using var attachment = output.Attach(source);
        using var receive = output.RegisterReceive();
        var message = isError
            ? FlowMessage.RestoreError(new FlowError("invalid", "Invalid input", "validation"),
                new MessageId("message"), new TraceId("trace"), DateTimeOffset.UnixEpoch,
                new CorrelationId("request"), new MessageId("cause"),
                new Dictionary<string, string> { ["origin"] = "test" })
            : FlowMessage.Restore(FlowValue.From(new { value = 42 }),
                new MessageId("message"), new TraceId("trace"), DateTimeOffset.UnixEpoch,
                new CorrelationId("request"), new MessageId("cause"),
                new Dictionary<string, string> { ["origin"] = "test" });

        source.Post(message).ShouldBeTrue();

        (await receive.Task.WaitAsync(TestTimeout)).Message.ShouldBeSameAs(message);
        capture.Messages.ShouldHaveSingleItem().ShouldBeSameAs(message);
        output.Complete();
        await output.Completion.WaitAsync(TestTimeout);
    }

    private static void AssertProjection(FlowMessage actual, FlowMessage expected)
        => actual.ShouldBeSameAs(expected);

    private static void AssertProjection(FlowMessage<FlowValue>? actual, FlowMessage expected)
    {
        actual.ShouldNotBeNull().ShouldBe(expected.IsError
            ? expected.ConvertError<FlowValue>()
            : expected.ConvertValue<FlowValue>(expected.Value!));
        if (!expected.IsError)
            actual.Value.ShouldBeSameAs(expected.Value);
        actual.Headers.ShouldBeSameAs(expected.Headers);
    }

    private static TimeSpan TestTimeout => TimeSpan.FromSeconds(5);

    private static ApplicationOutputPort CreateOutput(
        ApplicationRevisionRouting routing,
        IApplicationOutputCapture? capture = null,
        Action<ApplicationPortRejection>? report = null,
        int capacity = 4,
        bool discardUnmatched = false)
        => new(
            Output,
            capacity,
            report ?? (static _ => { }),
            static _ => { },
            routing,
            capture,
            discardUnmatched: discardUnmatched);

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
                outputs: [ComponentPorts.Metadata<FlowValue>("Output")]),
            new ComponentDescriptor(
                "sink",
                UnusedFactory,
                inputs: [ComponentPorts.Metadata<FlowValue>("Input")])
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
        private readonly Channel<FlowMessage> _delivered = Channel.CreateUnbounded<FlowMessage>();
        private readonly ConcurrentQueue<FlowMessage> _messages = new();

        public ApplicationAddress Source => source;

        public ApplicationAddress Target => target;

        public IReadOnlyList<FlowMessage> Messages => _messages.ToArray();

        public bool IsMatch(FlowMessage message) => true;

        public ValueTask DeliverAsync(FlowMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _messages.Enqueue(message);
            _delivered.Writer.TryWrite(message).ShouldBeTrue();
            return ValueTask.CompletedTask;
        }

        public async Task<FlowMessage> NextAsync()
        {
            using var timeout = new CancellationTokenSource(TestTimeout);
            return await _delivered.Reader.ReadAsync(timeout.Token);
        }
    }

    private sealed class GatedCapture : IApplicationOutputCapture
    {
        private readonly Channel<CaptureInvocation> _invocations =
            Channel.CreateUnbounded<CaptureInvocation>();
        private readonly ConcurrentQueue<FlowMessage> _messages = new();

        public IReadOnlyList<FlowMessage> Messages => _messages.ToArray();

        public ValueTask CaptureAsync(
            FlowMessage message,
            CancellationToken cancellationToken = default)
        {
            var invocation = new CaptureInvocation(message, cancellationToken);
            _messages.Enqueue(message);
            _invocations.Writer.TryWrite(invocation).ShouldBeTrue();
            return new ValueTask(invocation.Completion);
        }

        public async Task<CaptureInvocation> NextAsync()
        {
            using var timeout = new CancellationTokenSource(TestTimeout);
            return await _invocations.Reader.ReadAsync(timeout.Token);
        }
    }

    private sealed class CaptureInvocation
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CaptureInvocation(FlowMessage message, CancellationToken cancellationToken)
        {
            Message = message;
            Completion = _release.Task.WaitAsync(cancellationToken);
        }

        public FlowMessage Message { get; }

        public Task Completion { get; }

        public void Release() => _release.TrySetResult().ShouldBeTrue();
    }

    private sealed class ThrowingCapture(Exception exception) : IApplicationOutputCapture
    {
        private readonly ConcurrentQueue<FlowMessage> _messages = new();

        public IReadOnlyList<FlowMessage> Messages => _messages.ToArray();

        public ValueTask CaptureAsync(
            FlowMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _messages.Enqueue(message);
            return ValueTask.FromException(exception);
        }
    }

    private sealed class RecordingCapture : IApplicationOutputCapture
    {
        private readonly ConcurrentQueue<FlowMessage> _messages = new();

        public IReadOnlyList<FlowMessage> Messages => _messages.ToArray();

        public ValueTask CaptureAsync(
            FlowMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _messages.Enqueue(message);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingResolver(
        ApplicationAddress configuredAddress,
        IApplicationOutputCapture capture) : IApplicationOutputCaptureResolver
    {
        private readonly List<ApplicationAddress> _resolutions = [];

        public IReadOnlyList<ApplicationAddress> Resolutions => _resolutions;

        public IApplicationOutputCapture? Resolve(ApplicationAddress address)
        {
            _resolutions.Add(address);
            return address == configuredAddress
                ? capture
                : null;
        }
    }
}
