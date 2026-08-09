using System.Threading.Tasks.Dataflow;
using FluxFlow.Composition.Authoring;
using FluxFlow.Engine.Ports;
using FluxFlow.Nodes;
using Shouldly;
using Xunit;

namespace FluxFlow.Engine.Tests;

public sealed class ApplicationPortHandleTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Typed_handle_surface_is_thin_and_keeps_attachment_operations_internal()
    {
        var publicMethods = typeof(ApplicationPorts)
            .GetMethods()
            .Where(static method => method.DeclaringType == typeof(ApplicationPorts))
            .ToArray();

        AssertHandleOverload(publicMethods, nameof(ApplicationPorts.SendAsync), typeof(InputPortHandle<>));
        AssertHandleOverload(publicMethods, nameof(ApplicationPorts.SendAsync), typeof(SignalInputPortHandle));
        AssertHandleOverload(publicMethods, nameof(ApplicationPorts.ReceiveAsync), typeof(OutputPortHandle<>));
        AssertHandleOverload(publicMethods, nameof(ApplicationPorts.ObserveAsync), typeof(OutputPortHandle<>));
        publicMethods
            .Single(method =>
                method.Name == nameof(ApplicationPorts.SendAndReceiveAsync) &&
                IsGenericParameter(method.GetParameters()[0].ParameterType, typeof(InputPortHandle<>)))
            .GetParameters()[1].ParameterType.GetGenericTypeDefinition()
            .ShouldBe(typeof(OutputPortHandle<>));
        publicMethods.Select(static method => method.Name)
            .ShouldNotContain("AttachInputAsync");
        publicMethods.Select(static method => method.Name)
            .ShouldNotContain("AttachSignalInputAsync");
        publicMethods.Select(static method => method.Name)
            .ShouldNotContain("AttachOutput");
    }

    [Fact]
    public async Task Typed_handles_send_receive_observe_and_request_through_their_exact_addresses()
    {
        var handles = CreateHandles();
        await using var runtime = CreateRuntime(handles);
        var ports = new ApplicationPorts(() => runtime);
        var inputTarget = new BufferBlock<FlowMessage<string>>();
        var signalTarget = new RecordingSignalTarget();
        var outputSource = new BufferBlock<FlowMessage<string>>();
        var replySource = new BufferBlock<FlowMessage<string>>();
        var response = new TaskCompletionSource<FlowMessage<string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var requestTarget = new ActionBlock<FlowMessage<string>>(request =>
        {
            var reply = request.With($"reply:{request.Value}");
            response.TrySetResult(reply).ShouldBeTrue();
            replySource.Post(reply).ShouldBeTrue();
        });

        await using var inputAttachment = await runtime.AttachInputAsync(
            handles.Input,
            inputTarget);
        await using var signalAttachment = await runtime.AttachSignalInputAsync(
            handles.Signal,
            signalTarget);
        await using var requestAttachment = await runtime.AttachInputAsync(
            handles.Request,
            requestTarget);
        using var outputAttachment = runtime.AttachOutput(handles.Output, outputSource);
        using var replyAttachment = runtime.AttachOutput(handles.Reply, replySource);
        var observationResult = await ports.ObserveAsync(handles.Output, capacity: 2);
        await using var observation = observationResult.Observation.ShouldNotBeNull();
        var receive = ports.ReceiveAsync(handles.Output, TestTimeout);

        var inputMessage = FlowMessage.Restore(
            "input",
            new MessageId("input-message"),
            new TraceId("input-trace"),
            DateTimeOffset.Parse("2026-08-08T10:00:00+00:00"));
        var signalMessage = FlowMessage.Restore(
            42,
            new MessageId("signal-message"),
            new TraceId("signal-trace"),
            DateTimeOffset.Parse("2026-08-08T10:01:00+00:00"));
        var outputMessage = FlowMessage.Restore(
            "output",
            new MessageId("output-message"),
            new TraceId("output-trace"),
            DateTimeOffset.Parse("2026-08-08T10:02:00+00:00"));
        var request = FlowMessage.Restore(
            "question",
            new MessageId("request-message"),
            new TraceId("request-trace"),
            DateTimeOffset.Parse("2026-08-08T10:03:00+00:00"));

        var inputResult = await ports.SendAsync(handles.Input, inputMessage);
        var signalResult = await ports.SendAsync(handles.Signal, signalMessage);
        outputSource.Post(outputMessage).ShouldBeTrue();
        var requestResult = await ports.SendAndReceiveAsync(
            handles.Request,
            handles.Reply,
            request,
            TestTimeout);

        inputResult.Port.ShouldBe(handles.Input.Address);
        inputResult.Status.ShouldBe(PortSendStatus.Accepted);
        (await inputTarget.ReceiveAsync().WaitAsync(TestTimeout)).ShouldBeSameAs(inputMessage);
        signalResult.Port.ShouldBe(handles.Signal.Address);
        signalResult.Status.ShouldBe(PortSendStatus.Accepted);
        var receivedSignal = await signalTarget.Received.WaitAsync(TestTimeout);
        receivedSignal.Value.ShouldBe(signalMessage.Value);
        receivedSignal.MessageId.ShouldBe(signalMessage.MessageId);
        receivedSignal.TraceId.ShouldBe(signalMessage.TraceId);

        observationResult.Port.ShouldBe(handles.Output.Address);
        observationResult.Status.ShouldBe(PortObserveStatus.Started);
        observation.Port.ShouldBe(handles.Output.Address);
        observation.Capacity.ShouldBe(2);
        var received = await receive.WaitAsync(TestTimeout);
        received.Port.ShouldBe(handles.Output.Address);
        received.Status.ShouldBe(PortReceiveStatus.Received);
        received.Message.ShouldBeSameAs(outputMessage);
        (await observation.Messages.ReceiveAsync().WaitAsync(TestTimeout))
            .ShouldBeSameAs(outputMessage);

        var exactResponse = await response.Task.WaitAsync(TestTimeout);
        requestResult.InputPort.ShouldBe(handles.Request.Address);
        requestResult.OutputPort.ShouldBe(handles.Reply.Address);
        requestResult.Status.ShouldBe(PortRequestStatus.Received);
        requestResult.Response.ShouldBeSameAs(exactResponse);
        requestResult.Response!.Value.ShouldBe("reply:question");
        requestResult.Response.TraceId.ShouldBe(request.TraceId);
    }

    [Fact]
    public async Task Typed_handle_attachments_bind_message_signal_and_output_to_exact_runtime_ports()
    {
        var handles = CreateHandles();
        await using var runtime = CreateRuntime(handles);
        var inputTarget = new BufferBlock<FlowMessage<string>>();
        var signalTarget = new RecordingSignalTarget();
        var outputSource = new BufferBlock<FlowMessage<string>>();
        await using var inputAttachment = await runtime.AttachInputAsync(
            handles.Input,
            inputTarget);
        await using var signalAttachment = await runtime.AttachSignalInputAsync(
            handles.Signal,
            signalTarget);
        using var outputAttachment = runtime.AttachOutput(handles.Output, outputSource);
        var inputMessage = FlowMessage.Create("input");
        var signalMessage = FlowMessage.Create(new SignalPayload(17));
        var outputMessage = FlowMessage.Create("output");
        var receive = runtime.ReceiveAsync(handles.Output, TestTimeout);

        (await runtime.SendAsync(handles.Input, inputMessage)).Status
            .ShouldBe(PortSendStatus.Accepted);
        (await runtime.SendAsync(handles.Signal, signalMessage)).Status
            .ShouldBe(PortSendStatus.Accepted);
        outputSource.Post(outputMessage).ShouldBeTrue();

        (await inputTarget.ReceiveAsync().WaitAsync(TestTimeout)).ShouldBeSameAs(inputMessage);
        var receivedSignal = await signalTarget.Received.WaitAsync(TestTimeout);
        receivedSignal.Value.ShouldBe(signalMessage.Value);
        receivedSignal.TraceId.ShouldBe(signalMessage.TraceId);
        var output = await receive.WaitAsync(TestTimeout);
        output.Port.ShouldBe(handles.Output.Address);
        output.Message.ShouldBeSameAs(outputMessage);

        await inputAttachment.DisposeAsync();
        await signalAttachment.DisposeAsync();
        outputAttachment.Dispose();

        (await runtime.SendAsync(handles.Input, FlowMessage.Create("late"))).Status
            .ShouldBe(PortSendStatus.Unavailable);
        (await runtime.SendAsync(handles.Signal, FlowMessage.Create("late"))).Status
            .ShouldBe(PortSendStatus.Unavailable);
        (await runtime.ReceiveAsync(handles.Output)).Status
            .ShouldBe(PortReceiveStatus.Unavailable);
    }

    [Fact]
    public async Task Typed_handle_operations_preserve_timeout_cancellation_and_type_direction_errors()
    {
        var handles = CreateHandles();
        await using var runtime = CreateRuntime(handles);
        var ports = new ApplicationPorts(() => runtime);
        var idleOutput = new BufferBlock<FlowMessage<string>>();
        var idleReply = new BufferBlock<FlowMessage<string>>();
        var acceptedRequest = new BufferBlock<FlowMessage<string>>();
        using var idleAttachment = runtime.AttachOutput(handles.Output, idleOutput);
        using var replyAttachment = runtime.AttachOutput(handles.Reply, idleReply);
        await using var requestAttachment = await runtime.AttachInputAsync(
            handles.Request,
            acceptedRequest);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var sendCanceled = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await ports.SendAsync(handles.Input, FlowMessage.Create("value"), cancellation.Token));
        var signalCanceled = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await ports.SendAsync(handles.Signal, FlowMessage.Create("signal"), cancellation.Token));
        var receiveCanceled = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await ports.ReceiveAsync(handles.Output, cancellationToken: cancellation.Token));
        var observeCanceled = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await ports.ObserveAsync(handles.Output, cancellationToken: cancellation.Token));
        var requestCanceled = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await ports.SendAndReceiveAsync(
                handles.Request,
                handles.Reply,
                FlowMessage.Create("request"),
                cancellationToken: cancellation.Token));
        var inputAttachCanceled = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await runtime.AttachInputAsync(
                handles.Input,
                new BufferBlock<FlowMessage<string>>(),
                cancellation.Token));
        var signalAttachCanceled = await Should.ThrowAsync<OperationCanceledException>(async () =>
            await runtime.AttachSignalInputAsync(
                handles.Signal,
                new RecordingSignalTarget(),
                cancellation.Token));

        sendCanceled.CancellationToken.ShouldBe(cancellation.Token);
        signalCanceled.CancellationToken.ShouldBe(cancellation.Token);
        receiveCanceled.CancellationToken.ShouldBe(cancellation.Token);
        observeCanceled.CancellationToken.ShouldBe(cancellation.Token);
        requestCanceled.CancellationToken.ShouldBe(cancellation.Token);
        inputAttachCanceled.CancellationToken.ShouldBe(cancellation.Token);
        signalAttachCanceled.CancellationToken.ShouldBe(cancellation.Token);

        var timedOut = await ports.ReceiveAsync(
            handles.Output,
            TimeSpan.FromMilliseconds(20));
        timedOut.Port.ShouldBe(handles.Output.Address);
        timedOut.Status.ShouldBe(PortReceiveStatus.TimedOut);
        timedOut.Message.ShouldBeNull();
        var requestTimedOut = await ports.SendAndReceiveAsync(
            handles.Request,
            handles.Reply,
            FlowMessage.Create("unanswered"),
            TimeSpan.FromMilliseconds(20));
        requestTimedOut.InputPort.ShouldBe(handles.Request.Address);
        requestTimedOut.OutputPort.ShouldBe(handles.Reply.Address);
        requestTimedOut.Status.ShouldBe(PortRequestStatus.TimedOut);
        requestTimedOut.Response.ShouldBeNull();

        var wrongInputType = handles.Definition.Input<int>(handles.Input.Name);
        var wrongOutputType = handles.Definition.Output<int>(handles.Output.Name);
        var outputAsInput = handles.Definition.Input<string>(handles.Output.Name);
        var inputAsOutput = handles.Definition.Output<string>(handles.Input.Name);

        var inputTypeError = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await ports.SendAsync(wrongInputType, FlowMessage.Create(42)));
        var outputTypeError = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await ports.ReceiveAsync(wrongOutputType));
        var inputDirectionError = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await ports.SendAsync(outputAsInput, FlowMessage.Create("value")));
        var outputDirectionError = await Should.ThrowAsync<InvalidOperationException>(async () =>
            await ports.ReceiveAsync(inputAsOutput));

        inputTypeError.Message.ShouldContain(typeof(int).ToString());
        inputTypeError.Message.ShouldContain(typeof(string).ToString());
        outputTypeError.Message.ShouldContain(typeof(int).ToString());
        outputTypeError.Message.ShouldContain(typeof(string).ToString());
        inputDirectionError.Message.ShouldContain(handles.Output.Address.Value);
        outputDirectionError.Message.ShouldContain(handles.Input.Address.Value);

        (await Should.ThrowAsync<ArgumentNullException>(async () =>
            await ports.SendAsync((InputPortHandle<string>)null!, FlowMessage.Create("value"))))
            .ParamName.ShouldBe("input");
        (await Should.ThrowAsync<ArgumentNullException>(async () =>
            await ports.ReceiveAsync((OutputPortHandle<string>)null!)))
            .ParamName.ShouldBe("output");
    }

    [Fact]
    public async Task Address_based_application_port_overloads_remain_callable_and_behaviorally_identical()
    {
        var handles = CreateHandles();
        await using var runtime = CreateRuntime(handles);
        var ports = new ApplicationPorts(() => runtime);
        var first = FlowMessage.Create("first");
        var second = FlowMessage.Create("second");

        var typedSend = await ports.SendAsync(handles.Input, first);
        var addressSend = await ports.SendAsync(handles.Input.Address, second);
        var stringSend = await ports.SendAsync(handles.Input.Address.Value, second);
        var typedReceive = await ports.ReceiveAsync(handles.Output);
        var addressReceive = await ports.ReceiveAsync<string>(handles.Output.Address);
        var stringReceive = await ports.ReceiveAsync<string>(handles.Output.Address.Value);
        var typedObserve = await ports.ObserveAsync(handles.Output);
        var addressObserve = await ports.ObserveAsync<string>(handles.Output.Address);
        var stringObserve = await ports.ObserveAsync<string>(handles.Output.Address.Value);

        typedSend.ShouldBe(new PortSendResult
        {
            Port = handles.Input.Address,
            Status = PortSendStatus.Unavailable
        });
        addressSend.ShouldBe(typedSend);
        stringSend.ShouldBe(typedSend);
        typedReceive.Status.ShouldBe(PortReceiveStatus.Unavailable);
        addressReceive.ShouldBe(typedReceive);
        stringReceive.ShouldBe(typedReceive);
        typedObserve.Status.ShouldBe(PortObserveStatus.Unavailable);
        addressObserve.ShouldBe(typedObserve);
        stringObserve.ShouldBe(typedObserve);
    }

    private static ApplicationPortRuntime CreateRuntime(PortHandles handles)
        => new ApplicationPortRuntimeBuilder()
            .AddInput<string>(handles.Input.Address)
            .AddSignalInput(handles.Signal.Address)
            .AddInput<string>(handles.Request.Address)
            .AddOutput<string>(handles.Output.Address)
            .AddOutput<string>(handles.Reply.Address)
            .Build();

    private static void AssertHandleOverload(
        IEnumerable<System.Reflection.MethodInfo> methods,
        string methodName,
        Type firstParameter)
        => methods.Count(method =>
                method.Name == methodName &&
                IsGenericParameter(method.GetParameters()[0].ParameterType, firstParameter))
            .ShouldBe(1);

    private static bool IsGenericParameter(Type candidate, Type expected)
        => expected.IsGenericTypeDefinition
            ? candidate.IsGenericType && candidate.GetGenericTypeDefinition() == expected
            : candidate == expected;

    private static PortHandles CreateHandles()
    {
        var application = new ApplicationDefinitionBuilder();
        var definition = application
            .AddWorkflow("main")
            .AddComponent("node", "test.ports");
        return new PortHandles(
            definition,
            definition.Input<string>("Input"),
            definition.SignalInput("Signal"),
            definition.Input<string>("Request"),
            definition.Output<string>("Output"),
            definition.Output<string>("Reply"));
    }

    private sealed record PortHandles(
        ComponentHandle Definition,
        InputPortHandle<string> Input,
        SignalInputPortHandle Signal,
        InputPortHandle<string> Request,
        OutputPortHandle<string> Output,
        OutputPortHandle<string> Reply);

    private sealed record SignalPayload(int Value);

    private sealed class RecordingSignalTarget : IFlowSignalTarget
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<ReceivedSignal> _received =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completion => _completion.Task;

        public Task<ReceivedSignal> Received => _received.Task;

        public ValueTask<bool> SendAsync<T>(
            FlowMessage<T> signal,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_received.TrySetResult(new ReceivedSignal(
                signal.Value,
                signal.MessageId,
                signal.TraceId)));
        }
    }

    private sealed record ReceivedSignal(object? Value, MessageId MessageId, TraceId TraceId);
}
