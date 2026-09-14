using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading.Tasks.Dataflow;
using FluxFlow.Data;
using Shouldly;
using Xunit;

namespace FluxFlow.Nodes.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessingTraceCollection
{
    public const string Name = "Processing trace listeners";
}

[Collection(ProcessingTraceCollection.Name)]
public sealed class FlowProcessingTraceTests
{
    private const string SourceName = "FluxFlow.Core";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Suppression_restores_only_running_ambient_activity(bool stopSavedActivity)
    {
        using var previous = StartProducer();
        var errors = 0;
        EventHandler<FirstChanceExceptionEventArgs> observer = (_, args) =>
        {
            if (args.Exception is InvalidOperationException && args.Exception.TargetSite?.DeclaringType == typeof(Activity))
                Interlocked.Increment(ref errors);
        };
        AppDomain.CurrentDomain.FirstChanceException += observer;
        try
        {
            using (FlowTracing.SuppressContext())
            {
                Activity.Current.ShouldBeNull();
                await Task.Run(() => { if (stopSavedActivity) previous.Stop(); }).WaitAsync(Deadline);
                Activity.Current.ShouldBeNull();
            }

            Activity.Current.ShouldBeSameAs(stopSavedActivity ? null : previous);
            errors.ShouldBe(0);
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= observer; }
    }

    [Theory]
    [InlineData(false, "off", false)]
    [InlineData(false, "off", true)]
    [InlineData(false, "None", false)]
    [InlineData(false, "None", true)]
    [InlineData(false, "PropagationData", false)]
    [InlineData(false, "PropagationData", true)]
    [InlineData(false, "AllDataAndRecorded", false)]
    [InlineData(false, "AllDataAndRecorded", true)]
    [InlineData(true, "off", false)]
    [InlineData(true, "off", true)]
    [InlineData(true, "None", false)]
    [InlineData(true, "None", true)]
    [InlineData(true, "PropagationData", false)]
    [InlineData(true, "PropagationData", true)]
    [InlineData(true, "AllDataAndRecorded", false)]
    [InlineData(true, "AllDataAndRecorded", true)]
    public async Task Processing_restores_only_running_ambient_activity(bool aggregate, string sampling, bool stopSavedActivity)
    {
        using var traces = sampling == "off" ? null : new Traces(Enum.Parse<ActivitySamplingResult>(sampling));
        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("payload", new JsonSerializerOptions(JsonSerializerDefaults.Web))), headers:
            ImmutableDictionary<string, string>.Empty.Add("traceparent",
                "00-11111111111111111111111111111111-2222222222222222-01"));
        using var previous = StartProducer();
        var errors = 0;
        var createsActivity = sampling is "PropagationData" or "AllDataAndRecorded";
        Activity? processing = null;
        EventHandler<FirstChanceExceptionEventArgs> observer = (_, args) =>
        {
            if (args.Exception is InvalidOperationException && args.Exception.TargetSite?.DeclaringType == typeof(Activity))
                Interlocked.Increment(ref errors);
        };
        AppDomain.CurrentDomain.FirstChanceException += observer;
        try
        {
            using (aggregate ? FlowTracing.StartProcessing([message], this) : FlowTracing.StartProcessing(message, this))
            {
                processing = Activity.Current;
                if (createsActivity)
                {
                    processing.ShouldNotBeNull();
                    processing.OperationName.ShouldBe("flow.process");
                    processing.ParentSpanId.ShouldBe(aggregate ? default : Context(message).SpanId);
                    processing.Links.Single().Context.ShouldBe(Context(message));
                }
                else processing.ShouldBeNull();

                await Task.Run(() => { if (stopSavedActivity) previous.Stop(); }).WaitAsync(Deadline);
                Activity.Current.ShouldBeSameAs(processing);
            }

            Activity.Current.ShouldBeSameAs(stopSavedActivity ? null : previous);
            errors.ShouldBe(0);
            if (createsActivity)
            {
                processing!.IsStopped.ShouldBeTrue();
                traces!.Started.Single().ShouldBeSameAs(processing);
                traces.Stopped.Single().ShouldBeSameAs(processing);
            }
            else (traces?.Started.Count ?? 0).ShouldBe(0);
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= observer; }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void New_messages_capture_current_context_without_mutating_caller_headers(bool typed, bool error)
    {
        using var producer = StartProducer();
        var headers = new Dictionary<string, string> { ["custom"] = "original" };
        var businessTrace = TraceId.New();
        var failure = new FlowError("test.failure", "Failure", "test");
        var message = typed
            ? error
                ? FlowMessage.CreateError<string>(failure, traceId: businessTrace, headers: headers).ConvertError()
                : FlowMessage.Create("value", traceId: businessTrace, headers: headers).ConvertValue((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("value", new JsonSerializerOptions(JsonSerializerDefaults.Web))))
            : error
                ? FlowMessage.CreateError(failure, traceId: businessTrace, headers: headers)
                : FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("value", new JsonSerializerOptions(JsonSerializerDefaults.Web))), traceId: businessTrace, headers: headers);

        Context(message).ShouldBe(producer.Context);
        message.TraceId.ShouldBe(businessTrace);
        message.IsError.ShouldBe(error);
        headers.Count.ShouldBe(1);
        headers["custom"] = "changed";
        message.Headers["custom"].ShouldBe("original");
        message.Headers["tracestate"].ShouldBe("region=west");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Explicit_context_and_identity_preserving_operations_keep_their_context(bool error)
    {
        using var producer = StartProducer();
        var creationContext = producer.Context;
        var supplied = ImmutableDictionary.Create<string, string>(StringComparer.Ordinal)
            .Add("traceparent", producer.Id!)
            .Add("tracestate", producer.TraceStateString!);
        using var unrelated = new Activity("unrelated").SetIdFormat(ActivityIdFormat.W3C).Start();
        var message = error
            ? FlowMessage.CreateError(new FlowError("test.failure", "Failure", "test"), headers: supplied)
            : FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("value", new JsonSerializerOptions(JsonSerializerDefaults.Web))), headers: supplied);

        Context(message).ShouldBe(creationContext);
        message.Headers.ShouldBeSameAs(supplied);
        var typed = error ? message.ConvertError<string>() : message.ConvertValue("value");
        var canonical = error ? typed.ConvertError() : typed.ConvertValue((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("value", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        canonical.Headers.ShouldBeSameAs(message.Headers);
        canonical.MessageId.ShouldBe(message.MessageId);
        Context(canonical).ShouldBe(creationContext);
        var restored = JsonSerializer.Deserialize<FlowMessage>(JsonSerializer.Serialize(message))!;
        Context(restored).ShouldBe(creationContext);
        restored.MessageId.ShouldBe(message.MessageId);

        var derived = message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("derived", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        Context(derived).ShouldBe(unrelated.Context);
        derived.CausationId.ShouldBe(message.MessageId);
        Context(message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("explicit", new JsonSerializerOptions(JsonSerializerDefaults.Web))), supplied)).ShouldBe(creationContext);
        var typedDerived = typed.With("typed-derived").ConvertValue((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement("typed-derived", new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        Context(typedDerived).ShouldBe(unrelated.Context);
        Context(typed.WithError<string>(new FlowError("derived.error", "Failure", "test")).ConvertError())
            .ShouldBe(unrelated.Context);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Untraced_creation_keeps_headers_and_adds_no_trace_context(bool hierarchical)
    {
        Activity.Current.ShouldBeNull();
        using var legacy = hierarchical ? new Activity("legacy").SetIdFormat(ActivityIdFormat.Hierarchical).Start() : null;
        var headers = ImmutableDictionary.Create<string, string>(StringComparer.Ordinal).Add("custom", "original");
        var initial = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))), headers: headers);
        var derived = initial.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(2, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var empty = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(3, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        initial.Headers.ShouldBeSameAs(headers);
        derived.Headers.ShouldBeSameAs(headers);
        initial.Headers.ContainsKey("traceparent").ShouldBeFalse();
        empty.Headers.ShouldBeEmpty();
        derived.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Processing_span_covers_awaited_work_and_uses_message_context(bool transform)
    {
        using var traces = new Traces();
        FlowMessage original;
        ActivityContext producerContext;
        using (var producer = StartProducer())
        {
            original = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            producerContext = producer.Context;
        }
        using var background = new Activity("background").SetIdFormat(ActivityIdFormat.W3C).Start();
        var entered = new TaskCompletionSource<Activity?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var afterAwait = new TaskCompletionSource<Activity?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var component = Probe(transform, async (message, token) =>
        {
            entered.TrySetResult(Activity.Current);
            await release.Task.WaitAsync(token);
            afterAwait.TrySetResult(Activity.Current);
            return message;
        });
        var sink = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var link = component.Ports.Outputs[FlowPorts.OutputName].LinkTo(sink);
        try
        {
            (await component.Ports.Inputs[FlowPorts.InputName].SendAsync(original)).ShouldBeTrue();
            var active = (await entered.Task.WaitAsync(Deadline)).ShouldNotBeNull();
            active.Source.Name.ShouldBe(SourceName);
            active.OperationName.ShouldBe("flow.process");
            active.Kind.ShouldBe(ActivityKind.Consumer);
            active.TraceId.ShouldBe(producerContext.TraceId);
            active.ParentSpanId.ShouldBe(producerContext.SpanId);
            active.TraceStateString.ShouldBe(producerContext.TraceState);
            active.Links.ShouldHaveSingleItem().Context.ShouldBe(producerContext);
            active.Duration.ShouldBe(TimeSpan.Zero);
            traces.Stopped.ShouldBeEmpty();
            Activity.Current.ShouldBeSameAs(background);

            release.TrySetResult();
            (await afterAwait.Task.WaitAsync(Deadline)).ShouldBeSameAs(active);
            (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(original);
            component.Complete();
            await component.Completion.WaitAsync(Deadline);
            traces.Stopped.ShouldHaveSingleItem().ShouldBeSameAs(active);
            active.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
            active.GetTagItem("flow.message_id").ShouldBe(original.MessageId.Value);
            active.GetTagItem("flow.trace_id").ShouldBe(original.TraceId.Value);
            Activity.Current.ShouldBeSameAs(background);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task Fanout_preserves_envelope_and_derived_messages_capture_processing_context()
    {
        using var traces = new Traces();
        FlowMessage original;
        ActivityContext producerContext;
        using (var producer = StartProducer())
        {
            original = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            producerContext = producer.Context;
        }
        var received = new ConcurrentQueue<FlowMessage>();
        var contexts = new ConcurrentQueue<ActivityContext>();
        Task<FlowMessage> Derive(FlowMessage message, CancellationToken _)
        {
            received.Enqueue(message);
            contexts.Enqueue(Activity.Current?.Context ?? default);
            return Task.FromResult(message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(43, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        }
        await using var first = Probe(false, Derive);
        await using var second = Probe(true, Derive);
        await using var source = new FlowOutput<FlowMessage>(new() { Capacity = 4 });
        using var firstLink = source.LinkTo(first.Ports.Inputs[FlowPorts.InputName], new() { PropagateCompletion = true });
        using var secondLink = source.LinkTo(second.Ports.Inputs[FlowPorts.InputName], new() { PropagateCompletion = true });
        var left = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        var right = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var leftLink = first.Ports.Outputs[FlowPorts.OutputName].LinkTo(left);
        using var rightLink = second.Ports.Outputs[FlowPorts.OutputName].LinkTo(right);

        (await source.SendAsync(original)).ShouldBeTrue();
        source.Complete();
        var outputs = new[] { await left.ReceiveAsync(Deadline), await right.ReceiveAsync(Deadline) };
        await Task.WhenAll(first.Completion, second.Completion, source.Completion).WaitAsync(Deadline);

        received.Count.ShouldBe(2);
        foreach (var message in received) message.ShouldBeSameAs(original);
        Context(original).ShouldBe(producerContext);
        contexts.Distinct().Count().ShouldBe(2);
        foreach (var message in outputs)
        {
            message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(43);
            message.CausationId.ShouldBe(original.MessageId);
            contexts.ShouldContain(Context(message));
        }
        traces.Stopped.Count.ShouldBe(2);
        foreach (var span in traces.Stopped)
        {
            span.ParentSpanId.ShouldBe(producerContext.SpanId);
            span.TraceId.ShouldBe(producerContext.TraceId);
        }
        Activity.Current.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Unparented_inputs_do_not_inherit_background_activity(bool transform, bool invalid)
    {
        using var traces = new Traces();
        var message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))), headers: invalid
            ? new Dictionary<string, string> { ["traceparent"] = "invalid", ["tracestate"] = "region=west" }
            : null);
        using var background = StartProducer();
        await using var component = Probe(transform, (input, _) => Task.FromResult(input));
        var sink = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var link = component.Ports.Outputs[FlowPorts.OutputName].LinkTo(sink);

        for (var index = 0; index < 2; index++)
        {
            (await component.Ports.Inputs[FlowPorts.InputName].SendAsync(message)).ShouldBeTrue();
            (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        }
        component.Complete();
        await component.Completion.WaitAsync(Deadline);

        traces.Stopped.Count.ShouldBe(2);
        traces.Stopped.Select(activity => activity.TraceId).Distinct().Count().ShouldBe(2);
        foreach (var span in traces.Stopped)
        {
            span.ParentSpanId.ShouldBe(default);
            span.TraceId.ShouldNotBe(background.TraceId);
            span.Links.ShouldBeEmpty();
            span.TraceStateString.ShouldBeNull();
        }
        Activity.Current.ShouldBeSameAs(background);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Processing_failure_and_cancellation_preserve_component_contracts(bool transform, bool cancel)
    {
        using var traces = new Traces();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("private failure details");
        var cancellation = new OperationCanceledException("stopping");
        await using var component = Probe(transform, async (message, token) =>
        {
            entered.TrySetResult();
            if (!cancel) throw failure;
            await release.Task.WaitAsync(token);
            return message;
        });
        var sink = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var link = component.Ports.Outputs[FlowPorts.OutputName].LinkTo(sink);
        try
        {
            (await component.Ports.Inputs[FlowPorts.InputName].SendAsync(FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))))))
                .ShouldBeTrue();
            await entered.Task.WaitAsync(Deadline);
            if (cancel)
            {
                component.Fault(cancellation);
                (await Should.ThrowAsync<OperationCanceledException>(() => component.Completion.WaitAsync(Deadline)))
                    .ShouldBeSameAs(cancellation);
                sink.Count.ShouldBe(0);
            }
            else
            {
                var error = await sink.ReceiveAsync(Deadline);
                error.Error.ShouldNotBeNull().Code.ShouldBe("node.processing_failed");
                error.Error.Message.ShouldBe(failure.Message);
                component.Complete();
                await component.Completion.WaitAsync(Deadline);
                component.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            }

            var span = traces.Stopped.ShouldHaveSingleItem();
            span.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
            span.Status.ShouldBe(cancel ? ActivityStatusCode.Unset : ActivityStatusCode.Error);
            span.GetTagItem("error.type").ShouldBe(cancel ? null : typeof(InvalidOperationException).FullName);
            span.GetTagItem("flow.processing.outcome").ShouldBe(cancel ? "canceled" : null);
            span.StatusDescription.ShouldBeNull();
            span.TagObjects.ShouldNotContain(pair => Equals(pair.Value, failure.Message));
            Activity.Current.ShouldBeNull();
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Incoming_error_is_forwarded_without_becoming_a_processing_exception(bool transform)
    {
        using var traces = new Traces();
        var calls = 0;
        await using var component = Probe(transform, (message, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(message);
        });
        var sink = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var link = component.Ports.Outputs[FlowPorts.OutputName].LinkTo(sink);
        var error = FlowMessage.CreateError(new FlowError("input.invalid", "Invalid input", "validation"));

        (await component.Ports.Inputs[FlowPorts.InputName].SendAsync(error)).ShouldBeTrue();
        (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(error);
        component.Complete();
        await component.Completion.WaitAsync(Deadline);

        calls.ShouldBe(0);
        var span = traces.Stopped.ShouldHaveSingleItem();
        span.GetTagItem("flow.message.is_error").ShouldBe(true);
        span.GetTagItem("error.type").ShouldBeNull();
        span.Status.ShouldBe(ActivityStatusCode.Unset);
        Activity.Current.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, "sample")]
    [InlineData(false, "start")]
    [InlineData(false, "stop")]
    [InlineData(true, "sample")]
    [InlineData(true, "start")]
    [InlineData(true, "stop")]
    public async Task Throwing_activity_callbacks_do_not_fault_processing_or_leak_context(bool transform, string failure)
    {
        using var traces = new Traces(failure: failure);
        var contexts = new ConcurrentQueue<Activity?>();
        await using var component = Probe(transform, (message, _) =>
        {
            contexts.Enqueue(Activity.Current);
            return Task.FromResult(message);
        });
        var sink = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var link = component.Ports.Outputs[FlowPorts.OutputName].LinkTo(sink);

        for (var index = 0; index < 2; index++)
        {
            FlowMessage message;
            using (var producer = StartProducer()) message = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(index, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            (await component.Ports.Inputs[FlowPorts.InputName].SendAsync(message)).ShouldBeTrue();
            (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(message);
        }
        component.Complete();
        await component.Completion.WaitAsync(Deadline);

        component.Completion.IsCompletedSuccessfully.ShouldBeTrue();
        traces.SampleCount.ShouldBe(2);
        traces.Started.Count.ShouldBe(failure == "sample" ? 0 : 2);
        traces.Stopped.Count.ShouldBe(failure == "sample" ? 0 : 2);
        contexts.Count.ShouldBe(2);
        foreach (var context in contexts)
        {
            if (failure == "stop") context.ShouldNotBeNull().Source.Name.ShouldBe(SourceName);
            else context.ShouldBeNull();
        }
        Activity.Current.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, (int)ActivitySamplingResult.None)]
    [InlineData(false, (int)ActivitySamplingResult.PropagationData)]
    [InlineData(false, (int)ActivitySamplingResult.AllDataAndRecorded)]
    [InlineData(true, -1)]
    [InlineData(true, (int)ActivitySamplingResult.None)]
    [InlineData(true, (int)ActivitySamplingResult.PropagationData)]
    [InlineData(true, (int)ActivitySamplingResult.AllDataAndRecorded)]
    public async Task Sampling_controls_processing_span_creation_and_auxiliary_tags(bool transform, int sampling)
    {
        using var traces = sampling < 0 ? null : new Traces((ActivitySamplingResult)sampling);
        Activity? observed = null;
        await using var component = Probe(transform, (message, _) =>
        {
            observed = Activity.Current;
            return Task.FromResult(message);
        });
        var sink = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var link = component.Ports.Outputs[FlowPorts.OutputName].LinkTo(sink);
        FlowMessage original;
        ActivityContext producerContext;
        using (var producer = StartProducer())
        {
            // An unsampled parent isolates the listener's choice on both supported runtimes.
            producer.ActivityTraceFlags = ActivityTraceFlags.None;
            original = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(42, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            producerContext = producer.Context;
        }

        (await component.Ports.Inputs[FlowPorts.InputName].SendAsync(original)).ShouldBeTrue();
        (await sink.ReceiveAsync(Deadline)).ShouldBeSameAs(original);
        component.Complete();
        await component.Completion.WaitAsync(Deadline);

        if (sampling <= (int)ActivitySamplingResult.None)
        {
            observed.ShouldBeNull();
            if (traces is not null)
            {
                traces.SampleCount.ShouldBe(1);
                traces.Started.ShouldBeEmpty();
                traces.Stopped.ShouldBeEmpty();
            }
        }
        else
        {
            var span = traces!.Stopped.ShouldHaveSingleItem();
            observed.ShouldBeSameAs(span);
            span.TraceId.ShouldBe(producerContext.TraceId);
            span.ParentSpanId.ShouldBe(producerContext.SpanId);
            var fullData = sampling == (int)ActivitySamplingResult.AllDataAndRecorded;
            span.IsAllDataRequested.ShouldBe(fullData);
            span.Recorded.ShouldBe(fullData);
            if (fullData) span.GetTagItem("flow.message_id").ShouldBe(original.MessageId.Value);
            else span.TagObjects.ShouldBeEmpty();
        }
        Activity.Current.ShouldBeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Disabled_processing_keeps_message_context_instead_of_background_context(bool transform, bool sampledOut)
    {
        using var traces = sampledOut ? new Traces(ActivitySamplingResult.None) : null;
        FlowMessage original;
        ActivityContext producerContext;
        using (var producer = StartProducer())
        {
            original = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            producerContext = producer.Context;
        }
        using var background = StartProducer();
        Activity? observed = background;
        await using var component = Probe(transform, (message, _) =>
        {
            observed = Activity.Current;
            return Task.FromResult(message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(2, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        });
        var sink = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 4 });
        using var link = component.Ports.Outputs[FlowPorts.OutputName].LinkTo(sink);

        (await component.Ports.Inputs[FlowPorts.InputName].SendAsync(original)).ShouldBeTrue();
        var output = await sink.ReceiveAsync(Deadline);
        component.Complete();
        await component.Completion.WaitAsync(Deadline);

        observed.ShouldBeNull();
        Context(output).ShouldBe(producerContext);
        output.Headers.ShouldBeSameAs(original.Headers);
        output.CausationId.ShouldBe(original.MessageId);
        output.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(2);
        Activity.Current.ShouldBeSameAs(background);
        if (traces is not null) traces.Stopped.ShouldBeEmpty();
    }

    [Fact]
    public void Derived_messages_drop_stale_trace_state_when_new_context_has_none()
    {
        FlowMessage original;
        using (var producer = StartProducer()) original = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(1, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        using var current = new Activity("new-context").SetIdFormat(ActivityIdFormat.W3C).Start();
        current.TraceStateString.ShouldBeNull();

        var value = original.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(2, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        var error = original.ConvertValue(1).WithError<int>(new FlowError("test.failure", "Failure", "test"))
            .ConvertError();

        Context(value).ShouldBe(current.Context);
        Context(error).ShouldBe(current.Context);
        value.Headers.ContainsKey("tracestate").ShouldBeFalse();
        error.Headers.ContainsKey("tracestate").ShouldBeFalse();
        original.Headers["tracestate"].ShouldBe("region=west");
        value.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(2);
        error.Error.ShouldNotBeNull().Code.ShouldBe("test.failure");
    }

    private static Activity StartProducer()
    {
        var activity = new Activity("producer").SetIdFormat(ActivityIdFormat.W3C).Start();
        activity.TraceStateString = "region=west";
        activity.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        return activity;
    }

    private static ActivityContext Context(FlowMessage message)
    {
        message.Headers.TryGetValue("traceparent", out var parent);
        message.Headers.TryGetValue("tracestate", out var state);
        ActivityContext.TryParse(parent, state, out var context).ShouldBeTrue();
        return context;
    }

    [Theory]
    [InlineData(false, -1, false)]
    [InlineData(false, -1, true)]
    [InlineData(false, (int)ActivitySamplingResult.None, false)]
    [InlineData(false, (int)ActivitySamplingResult.None, true)]
    [InlineData(false, (int)ActivitySamplingResult.PropagationData, false)]
    [InlineData(false, (int)ActivitySamplingResult.PropagationData, true)]
    [InlineData(false, (int)ActivitySamplingResult.AllDataAndRecorded, false)]
    [InlineData(false, (int)ActivitySamplingResult.AllDataAndRecorded, true)]
    [InlineData(true, -1, false)]
    [InlineData(true, -1, true)]
    [InlineData(true, (int)ActivitySamplingResult.None, false)]
    [InlineData(true, (int)ActivitySamplingResult.None, true)]
    [InlineData(true, (int)ActivitySamplingResult.PropagationData, false)]
    [InlineData(true, (int)ActivitySamplingResult.PropagationData, true)]
    [InlineData(true, (int)ActivitySamplingResult.AllDataAndRecorded, false)]
    [InlineData(true, (int)ActivitySamplingResult.AllDataAndRecorded, true)]
    public async Task Concurrent_fanin_keeps_each_message_context_across_await(bool transform, int sampling, bool sharedTrace)
    {
        using var traces = sampling < 0 ? null : new Traces((ActivitySamplingResult)sampling);
        var originals = new FlowMessage[2];
        var parents = new ActivityContext[2];
        using (var common = sharedTrace ? new Activity("common").SetIdFormat(ActivityIdFormat.W3C).Start() : null)
        {
            for (var index = 0; index < 2; index++)
            {
                using var producer = StartProducer();
                originals[index] = FlowMessage.Create((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(index, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
                parents[index] = producer.Context;
            }
        }
        parents[0].SpanId.ShouldNotBe(parents[1].SpanId);
        (parents[0].TraceId == parents[1].TraceId).ShouldBe(sharedTrace);
        using var background = new Activity("unrelated-pump").SetIdFormat(ActivityIdFormat.W3C).Start();
        var entered = Enumerable.Range(0, 2).Select(_ =>
            new TaskCompletionSource<Activity?>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var release = Enumerable.Range(0, 2).Select(_ =>
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var finished = Enumerable.Range(0, 2).Select(_ =>
            new TaskCompletionSource<FlowMessage>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        await using var component = Probe(transform, async (message, token) =>
        {
            var index = message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            message.ShouldBeSameAs(originals[index]);
            var current = Activity.Current;
            entered[index].TrySetResult(current);
            await release[index].Task.WaitAsync(token);
            Activity.Current.ShouldBeSameAs(current);
            var result = message.With((FlowValue)new JsonFlowValue(JsonSerializer.SerializeToElement(index + 10, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
            finished[index].TrySetResult(result);
            return result;
        }, parallelism: 2);
        await using var first = new FlowOutput<FlowMessage>(new() { Capacity = 2 });
        await using var second = new FlowOutput<FlowMessage>(new() { Capacity = 2 });
        var input = component.Ports.Inputs[FlowPorts.InputName];
        using var firstLink = first.LinkTo(input);
        using var secondLink = second.LinkTo(input);
        var sink = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 2 });
        using var outputLink = component.Ports.Outputs[FlowPorts.OutputName].LinkTo(sink);
        try
        {
            (await first.SendAsync(originals[0])).ShouldBeTrue();
            var left = await entered[0].Task.WaitAsync(Deadline);
            (await second.SendAsync(originals[1])).ShouldBeTrue();
            var right = await entered[1].Task.WaitAsync(Deadline);
            finished[0].Task.IsCompleted.ShouldBeFalse();
            finished[1].Task.IsCompleted.ShouldBeFalse();
            traces?.Stopped.ShouldBeEmpty();
            Activity.Current.ShouldBeSameAs(background);
            var active = new[] { left, right };
            for (var index = 0; index < 2; index++)
            {
                if (sampling <= (int)ActivitySamplingResult.None) active[index].ShouldBeNull();
                else
                {
                    var span = active[index].ShouldNotBeNull();
                    span.ParentSpanId.ShouldBe(parents[index].SpanId);
                    span.TraceId.ShouldBe(parents[index].TraceId);
                    span.Links.ShouldHaveSingleItem().Context.ShouldBe(parents[index]);
                    span.ParentSpanId.ShouldNotBe(background.SpanId);
                    span.Duration.ShouldBe(TimeSpan.Zero);
                    span.GetTagItem("flow.message_id").ShouldBe(sampling == (int)ActivitySamplingResult.AllDataAndRecorded
                        ? originals[index].MessageId.Value : null);
                }
            }
            if (left is not null) left.SpanId.ShouldNotBe(right!.SpanId);
            release[1].TrySetResult();
            await finished[1].Task.WaitAsync(Deadline);
            finished[0].Task.IsCompleted.ShouldBeFalse();
            release[0].TrySetResult();
            await finished[0].Task.WaitAsync(Deadline);
            first.Complete();
            second.Complete();
            await Task.WhenAll(first.Completion, second.Completion).WaitAsync(Deadline);
            component.Complete();
            var results = new[] { await sink.ReceiveAsync(Deadline), await sink.ReceiveAsync(Deadline) };
            await component.Completion.WaitAsync(Deadline);
            for (var index = 0; index < 2; index++)
            {
                var result = results.Single(message => message.Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) == index + 10);
                result.ShouldBeSameAs(await finished[index].Task);
                result.CausationId.ShouldBe(originals[index].MessageId);
                originals[index].Value!.Json.Deserialize<int>(new JsonSerializerOptions(JsonSerializerDefaults.Web)).ShouldBe(index);
                Context(originals[index]).ShouldBe(parents[index]);
                if (active[index] is { } span)
                {
                    Context(result).ShouldBe(span.Context);
                    span.Status.ShouldBe(ActivityStatusCode.Unset);
                }
                else result.Headers.ShouldBeSameAs(originals[index].Headers);
            }
            sink.Count.ShouldBe(0);
            component.Completion.IsCompletedSuccessfully.ShouldBeTrue();
            if (traces is not null)
            {
                traces.SampleCount.ShouldBe(2);
                traces.Started.Count.ShouldBe(sampling == (int)ActivitySamplingResult.None ? 0 : 2);
                traces.Stopped.ToArray().ShouldBe(traces.Started.ToArray(), ignoreOrder: true);
            }
            Activity.Current.ShouldBeSameAs(background);
        }
        finally
        {
            foreach (var gate in release) gate.TrySetResult();
            if (!component.Completion.IsCompleted) component.Fault(new OperationCanceledException("Test cleanup"));
        }
    }

    [Theory]
    [InlineData(false, -1, "drain")]
    [InlineData(false, -1, "fault")]
    [InlineData(false, -1, "decline")]
    [InlineData(false, 0, "drain")]
    [InlineData(false, 0, "fault")]
    [InlineData(false, 0, "decline")]
    [InlineData(false, 1, "drain")]
    [InlineData(false, 1, "fault")]
    [InlineData(false, 1, "decline")]
    [InlineData(false, 3, "drain")]
    [InlineData(false, 3, "fault")]
    [InlineData(false, 3, "decline")]
    [InlineData(true, -1, "drain")]
    [InlineData(true, -1, "fault")]
    [InlineData(true, -1, "decline")]
    [InlineData(true, 0, "drain")]
    [InlineData(true, 0, "fault")]
    [InlineData(true, 0, "decline")]
    [InlineData(true, 1, "drain")]
    [InlineData(true, 1, "fault")]
    [InlineData(true, 1, "decline")]
    [InlineData(true, 3, "drain")]
    [InlineData(true, 3, "fault")]
    [InlineData(true, 3, "decline")]
    public async Task Error_forwarding_scope_preserves_identity_admission_and_terminal_contract(
        bool handlesErrors, int sampling, string terminal)
    {
        var inputs = Enumerable.Range(0, 4).Select(index =>
        {
            using var producer = StartProducer();
            return FlowMessage.CreateError(new FlowError("input.invalid", $"Input {index}", "validation"));
        }).ToArray();
        var parent = Context(inputs[3]);
        var started = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<Activity>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var traces = sampling < 0 ? null : new Traces((ActivitySamplingResult)sampling,
            started: activity => { if (activity.ParentSpanId == parent.SpanId) started.TrySetResult(activity); },
            stopped: activity => { if (activity.ParentSpanId == parent.SpanId) stopped.TrySetResult(activity); });
        using var background = StartProducer();
        await using var node = new ErrorForwardProbe(handlesErrors);
        var target = new BufferBlock<FlowMessage>(new() { BoundedCapacity = 1 });
        using var link = node.Output.LinkTo(target, new() { PropagateCompletion = true });
        try
        {
            foreach (var input in inputs) (await node.Input.SendAsync(input).WaitAsync(Deadline)).ShouldBeTrue();
            node.Complete();
            Activity? held = sampling > 0 ? await started.Task.WaitAsync(Deadline) : null;
            stopped.Task.IsCompleted.ShouldBeFalse();
            node.Completion.IsCompleted.ShouldBeFalse();
            if (held is not null)
            {
                target.Count.ShouldBe(1);
                held.Duration.ShouldBe(TimeSpan.Zero);
                traces!.Stopped.Count.ShouldBe(3);
            }
            if (terminal == "drain")
            {
                (await target.ReceiveAsync().WaitAsync(Deadline)).ShouldBeSameAs(inputs[0]);
                if (held is not null)
                {
                    (await stopped.Task.WaitAsync(Deadline)).ShouldBeSameAs(held);
                    held.Status.ShouldBe(ActivityStatusCode.Unset);
                    held.GetTagItem("flow.processing.outcome").ShouldBeNull();
                    held.GetTagItem("error.type").ShouldBeNull();
                }
                node.Completion.IsCompleted.ShouldBeFalse();
                for (var index = 1; index < 4; index++)
                    (await target.ReceiveAsync().WaitAsync(Deadline)).ShouldBeSameAs(inputs[index]);
                await node.Completion.WaitAsync(Deadline);
                await target.Completion.WaitAsync(Deadline);
                target.TryReceive(out _).ShouldBeFalse();
                node.Calls.ShouldBe(handlesErrors ? 4 : 0);
            }
            else
            {
                var ownerFailure = new InvalidOperationException("Stop forwarding");
                if (terminal == "fault") node.Fault(ownerFailure);
                else node.Output.Complete();
                var failure = await Should.ThrowAsync<InvalidOperationException>(() => node.Completion.WaitAsync(Deadline));
                if (terminal == "fault") failure.ShouldBeSameAs(ownerFailure);
                if (held is not null)
                {
                    (await stopped.Task.WaitAsync(Deadline)).ShouldBeSameAs(held);
                    held.Status.ShouldBe(terminal == "fault" ? ActivityStatusCode.Unset : ActivityStatusCode.Error);
                    held.GetTagItem("flow.processing.outcome").ShouldBe(
                        sampling == 3 && terminal == "fault" ? "canceled" : null);
                    held.GetTagItem("error.type").ShouldBe(
                        sampling == 3 && terminal == "decline" ? typeof(InvalidOperationException).FullName : null);
                }
                var targetFailure = await Should.ThrowAsync<InvalidOperationException>(() => target.Completion.WaitAsync(Deadline));
                if (terminal == "fault") targetFailure.ShouldBeSameAs(ownerFailure);
                if (!handlesErrors) node.Calls.ShouldBe(0);
            }
            if (held is not null)
            {
                held.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
                held.ParentSpanId.ShouldBe(parent.SpanId);
                held.Links.Single().Context.ShouldBe(parent);
                held.GetTagItem("flow.message.is_error").ShouldBe(sampling == 3 ? true : null);
                held.StatusDescription.ShouldBeNull();
                held.TagObjects.ShouldNotContain(pair => Equals(pair.Value, inputs[3].Error!.Message));
                traces!.Stopped.ToArray().ShouldBe(traces.Started.ToArray());
            }
            else
            {
                traces?.Started.ShouldBeEmpty();
                traces?.Stopped.ShouldBeEmpty();
            }
            Activity.Current.ShouldBeSameAs(background);
            Context(inputs[3]).ShouldBe(parent);
        }
        finally
        {
            if (!node.Completion.IsCompleted) node.Fault(new OperationCanceledException("Test cleanup"));
        }
    }

    [Theory]
    [InlineData("sample")]
    [InlineData("start")]
    [InlineData("stop")]
    public async Task Listener_failures_preserve_error_bypass_identity(string failure)
    {
        using var traces = new Traces(failure: failure);
        using var background = StartProducer();
        await using var node = new ErrorForwardProbe(false);
        var target = new BufferBlock<FlowMessage>();
        using var link = node.Output.LinkTo(target, new() { PropagateCompletion = true });
        var inputs = Enumerable.Range(0, 2).Select(index =>
            FlowMessage.CreateError(new FlowError("input.invalid", $"Input {index}", "validation"))).ToArray();
        foreach (var input in inputs) (await node.Input.SendAsync(input).WaitAsync(Deadline)).ShouldBeTrue();
        node.Complete();
        await node.Completion.WaitAsync(Deadline);
        foreach (var input in inputs) (await target.ReceiveAsync().WaitAsync(Deadline)).ShouldBeSameAs(input);
        await target.Completion.WaitAsync(Deadline);
        node.Calls.ShouldBe(0);
        traces.SampleCount.ShouldBe(2);
        traces.Started.Count.ShouldBe(failure == "sample" ? 0 : 2);
        traces.Stopped.ToArray().ShouldBe(traces.Started.ToArray());
        target.TryReceive(out _).ShouldBeFalse();
        Activity.Current.ShouldBeSameAs(background);
    }

    private sealed class ErrorForwardProbe(bool handlesErrors)
        : FlowNode(new() { InputCapacity = 1, OutputCapacity = 1 })
    {
        public int Calls { get; private set; }
        protected override bool HandlesErrors => handlesErrors;
        protected override async Task ProcessAsync(FlowMessage message)
        {
            Calls++;
            await EmitAsync(message, Stopping);
        }
    }

    private static IFlowComponent Probe(bool transform, Func<FlowMessage, CancellationToken, Task<FlowMessage>> process,
        int parallelism = 1)
        => transform ? new TransformProbe(process, parallelism) : new ActionProbe(process, parallelism);

    private sealed class ActionProbe(Func<FlowMessage, CancellationToken, Task<FlowMessage>> process, int parallelism)
        : FlowNode(new() { InputCapacity = 4, OutputCapacity = 4, MaxDegreeOfParallelism = parallelism })
    {
        protected override async Task ProcessAsync(FlowMessage message)
            => await EmitAsync(await process(message, Stopping), Stopping);
    }

    private sealed class TransformProbe(Func<FlowMessage, CancellationToken, Task<FlowMessage>> process, int parallelism)
        : FlowTransformNode(new() { InputCapacity = 4, OutputCapacity = 4, MaxDegreeOfParallelism = parallelism })
    {
        protected override Task<FlowMessage> TransformAsync(FlowMessage message) => process(message, Stopping);
    }

    private sealed class Traces : IDisposable
    {
        private readonly ActivityListener _listener;
        private int _sampleCount;
        public int SampleCount => Volatile.Read(ref _sampleCount);
        public ConcurrentQueue<Activity> Started { get; } = new();
        public ConcurrentQueue<Activity> Stopped { get; } = new();

        public Traces(ActivitySamplingResult sampling = ActivitySamplingResult.AllDataAndRecorded, string? failure = null,
            Action<Activity>? started = null, Action<Activity>? stopped = null)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                {
                    Interlocked.Increment(ref _sampleCount);
                    if (failure == "sample") throw new InvalidOperationException("sample callback failed");
                    return sampling;
                },
                ActivityStarted = activity =>
                {
                    Started.Enqueue(activity);
                    started?.Invoke(activity);
                    if (failure == "start") throw new InvalidOperationException("start callback failed");
                },
                ActivityStopped = activity =>
                {
                    Stopped.Enqueue(activity);
                    stopped?.Invoke(activity);
                    if (failure == "stop") throw new InvalidOperationException("stop callback failed");
                }
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose() => _listener.Dispose();
    }
}
